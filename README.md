# transferit

Send files between two computers on the same network — **just pick a port number**.
No IP addresses, no accounts, no cloud. Files go straight from one machine to the other over your Wi-Fi / LAN.

```
 sender                                   receiver
 ──────                                   ────────
 $ transferit send                        $ transferit receive
 Port to send to: 5555                    Port to listen on: 5555
 Looking for a receiver on port 5555...   Waiting for a sender on port 5555...
 Found receiver at 192.168.1.42           Connected: 192.168.1.17:48684
 Sent photo.jpg (2411520 bytes)           Received photo.jpg (2411520 bytes)
 Sent notes.txt (812 bytes)               Received notes.txt (812 bytes)
```

---

## Install (Linux)

### Option A — prebuilt (no .NET needed)

Download `transferit-linux-x64.tar.gz` from the [**Releases**](../../releases) page
(use `linux-arm64` for a Raspberry Pi or other ARM machine), then:

```bash
tar -xzf transferit-linux-x64.tar.gz
cd transferit-linux-x64
./install.sh
```

### Option B — from source (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download))

```bash
git clone https://github.com/tanilhamdi/transferIt.git
cd transferIt
./install.sh
```

### What the installer does

1. Installs the `transferit` command to `~/.local/bin` (no `sudo`).
2. Adds `~/.local/bin` to your `PATH` if needed.
3. Creates your config files and asks where received files should be saved
   (default: `~/Downloads/transferit`).
4. If a firewall (ufw / firewalld) is running, offers to open a port for receiving.

Open a new terminal afterwards so the `transferit` command is found.

**Uninstall:** `./install.sh --uninstall` (your configs in `~/.config/transferit` are kept).

---

## Usage

| Command | What it does |
|---|---|
| `transferit add <file> [...]` | Add files to your send list |
| `transferit list` | Show the send list and the save folder |
| `transferit clear` | Empty the send list |
| `transferit savedir <folder>` | Set where received files are saved |
| `transferit receive` | Wait for a sender and save its files |
| `transferit send` | Send everything in the send list |

### Sending files to a friend

Both computers must be on the **same network** (same Wi-Fi / router).

**Receiver** (your friend):
```bash
transferit receive
# Port to listen on: 5555
```

**Sender** (you):
```bash
transferit add ~/Pictures/photo.jpg notes.txt
transferit send
# Port to send to: 5555
```

Use the same port on both sides. Any number from 1024 to 65535 that isn't in use works.
The receiver handles one transfer and then exits — run it again for the next one.

---

## Configuration

Configs live in `~/.config/transferit/` and are created automatically.
You normally edit them with the commands above, but they're plain text:

| File | Contents |
|---|---|
| `sender.cfg` | One full file path per line — the files `send` will send |
| `receiver.cfg` | One line — the folder received files are saved to |

If `receiver.cfg` is empty when you run `receive`, you'll be asked for a folder once.

---

## Troubleshooting

**`No receiver found on port …`**
- Is `transferit receive` running on the other computer, with the **same port**?
- Are both computers on the **same network**? It won't work across the internet,
  or between a phone hotspot and home Wi-Fi.
- **Firewall on the receiver.** Open the port (both TCP and UDP are needed):
  ```bash
  sudo ufw allow 5555                                   # Ubuntu / Mint / Arch with ufw
  sudo firewall-cmd --permanent --add-port=5555/tcp \
                    --add-port=5555/udp && sudo firewall-cmd --reload   # Fedora
  ```
  Only the **receiving** computer needs this. The sender needs nothing.
- **Guest / public / school Wi-Fi** often blocks devices from seeing each other
  ("client isolation"). Use a normal home network.

**`transferit: command not found`** — open a new terminal, or run `~/.local/bin/transferit`.

**`Address already in use`** — another program is using that port. Pick a different number.

---

## How it works

1. **Discovery (UDP).** The sender broadcasts a small "is there a receiver on port X?"
   message to the whole local network. The receiver answers, and the sender learns
   its IP from the reply. That's why you only need the port.
2. **Transfer (TCP).** The sender connects to that IP on the same port and streams the files:

   ```
   int32   number of files
   per file:
     int32   name length (bytes)
     byte[]  file name (UTF-8, no folders)
     int64   file size
     byte[]  file contents
   ```
   All integers are little-endian. Files are streamed, so large files don't use much memory.

---

## Security

transferit is meant for **networks you trust**, like your home Wi-Fi.

- There's **no password and no encryption**. While a receiver is waiting, anyone on the
  same network could send it files, and someone on the network could read the transfer.
- The receiver only accepts **one** transfer and then stops listening.
- Incoming file names are stripped of folders, so a sender can't write outside your save folder.
- Files with the same name as one already in the save folder are **overwritten**.

---

## Building

```bash
dotnet run -- <command>   # run from source, e.g. dotnet run -- receive
./build.sh                # standalone packages -> dist/transferit-linux-{x64,arm64}.tar.gz
```

The packages from `build.sh` are what you upload to a GitHub Release.

### Project layout

| File | Purpose |
|---|---|
| `Program.cs` | Command-line entry point |
| `Sender.cs` | Reads the send list, finds the receiver, sends files |
| `Receiver.cs` | Listens, receives files, saves them |
| `Discovery.cs` | UDP broadcast so the sender can find the receiver |
| `Config.cs` | Reads/writes `~/.config/transferit/` |
| `Input.cs` | Port prompt |
| `install.sh` / `build.sh` | Installer and release packaging |
