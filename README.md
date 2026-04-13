# FrogOnline

Online co-op for the local 2-player Unity game *Froggin' Around* (Steam demo).
Adds netcode by injecting a BepInEx plugin into each player's game and routing
traffic through a small dedicated relay server.

## Layout

| Folder              | What it is                                                        |
|---------------------|-------------------------------------------------------------------|
| `FrogOnline/`       | BepInEx 5 plugin loaded into the game on each player's machine.   |
| `FrogServer/`       | .NET 8 console app — UDP relay + 2-slot room matchmaker.          |
| `FrogOnline.Shared/`| Wire-protocol types referenced by both sides.                     |
| `install-client.sh` | One-shot installer: builds plugin, drops BepInEx + DLLs into the game folder. |

How it works at runtime: the host runs an authoritative simulation for both
frogs and broadcasts snapshots; the guest predicts its own frog locally and
applies snapshots for the remote one. Inputs flow guest → host. The relay
server matchmakes 2-player rooms and blindly forwards game payloads — it never
inspects them.

## Building

```bash
dotnet build
```

Outputs:
- `FrogOnline/bin/Debug/FrogOnline.dll` (+ `LiteNetLib.dll`, `FrogOnline.Shared.dll`) — the plugin
- `FrogServer/bin/Debug/net8.0/FrogServer.dll` — the relay

## Running the relay

```bash
dotnet run --project FrogServer -- --port 27015
```

To deploy on a VPS, publish a single-file Linux binary first:

```bash
dotnet publish FrogServer -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true
scp FrogServer/bin/Release/net8.0/linux-x64/publish/FrogServer user@host:~/frogserver
ssh user@host 'sudo ufw allow 27015/udp && ~/frogserver --port 27015'
```

Optional systemd unit example is in `install-client.sh`'s commit history.

## Installing the client (Linux + Steam/Proton)

```bash
./install-client.sh
SERVER_HOST=HOST_IP SERVER_PORT=27015 ./install-client.sh   # override default
GAME_DIR="/path/to/Froggin' Around Demo" ./install-client.sh       # override path
```

The script:
1. Auto-locates the game via Steam library VDFs.
2. Builds the plugin in Release.
3. Downloads BepInEx 5.4.23.2 (Windows x64 — correct for Proton) into the game folder if missing.
4. Copies the plugin DLLs to `BepInEx/plugins/FrogOnline/`.
5. Writes a pre-seeded config to `BepInEx/config/org.froggin.online.cfg`.

After installing, set the Steam launch options for the game so Proton loads BepInEx:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## Playing

1. Launch the game on both machines.
2. Press **F9** to open the lobby overlay.
3. Click **Connect** to reach the relay.
4. One player clicks **Create Room** → gets a 4-letter code; the other types it
   into **Join Room**.
5. Both players press their join button (Space / South button) on the
   PlayerJoinScreen. The host then presses Start; the guest follows
   automatically.

The HUD in the top-right shows connection phase, ping, snapshot tick, and
input flow status while in a room.
