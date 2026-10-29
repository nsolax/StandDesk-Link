"""Capture readable BLE state and known DPG read queries; never move or set settings."""
import asyncio
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

from bleak import BleakClient

ADDRESS = "D8:81:2F:0B:F2:7F"
DPG = "99fa0011-338a-1024-8a49-009c0215f78a"
QUERIES = {8: "product_info", 10: "setup", 128: "capabilities",
           129: "desk_offset", 134: "user_id", 135: "reminder_time",
           136: "reminder_setting"}


async def main():
    snapshot = {"captured_utc": datetime.now(timezone.utc).isoformat(),
                "address": ADDRESS, "type": "BLE settings snapshot, NOT firmware backup",
                "services": [], "dpg_queries": {}}
    async with BleakClient(ADDRESS, timeout=20) as client:
        for service in client.services:
            entry = {"uuid": service.uuid, "characteristics": []}
            for char in service.characteristics:
                item = {"uuid": char.uuid, "properties": char.properties}
                if "read" in char.properties:
                    try:
                        item["hex"] = bytes(await asyncio.wait_for(client.read_gatt_char(char), 5)).hex()
                    except Exception as exc:
                        item["error"] = str(exc)
                entry["characteristics"].append(item)
            snapshot["services"].append(entry)
        queue = asyncio.Queue()
        await client.start_notify(DPG, lambda _, data: queue.put_nowait(bytes(data)))

        async def query(command, label):
            while not queue.empty():
                queue.get_nowait()
            request = bytes([0x7F, command, 0])  # DPG read request only
            result = {"request_hex": request.hex()}
            try:
                await client.write_gatt_char(DPG, request, response=True)
                response = await asyncio.wait_for(queue.get(), 4)
                result["response_hex"] = response.hex()
                if len(response) >= 2 and response[0] == 1:
                    result["payload_hex"] = response[2:].hex()
            except Exception as exc:
                result["error"] = str(exc) or type(exc).__name__
            snapshot["dpg_queries"][label] = result
            print(label, result, flush=True)
            return result

        for command, label in QUERIES.items():
            await query(command, label)
        cap = snapshot["dpg_queries"]["capabilities"].get("payload_hex", "")
        if cap:
            flags = bytes.fromhex(cap)[0]
            snapshot["capabilities_decoded_community_protocol"] = {
                "memory_slots": flags & 7, "auto_up_flag": bool(flags & 8),
                "auto_down_flag": bool(flags & 16), "bluetooth_allowed": bool(flags & 32),
                "has_display": bool(flags & 64), "has_light": bool(flags & 128)}
            for slot in range(1, min(flags & 7, 4) + 1):
                await query(136 + slot, f"memory_{slot}")
        await client.stop_notify(DPG)
    folder = Path("backups") / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    folder.mkdir(parents=True, exist_ok=False)
    target = folder / "desk-2079-settings.json"
    target.write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
    digest = hashlib.sha256(target.read_bytes()).hexdigest()
    (folder / "SHA256.txt").write_text(f"{digest}  {target.name}\n", encoding="utf-8")
    print("Saved:", target.resolve(), flush=True)
    print(json.dumps(snapshot.get("capabilities_decoded_community_protocol")), flush=True)


if __name__ == "__main__":
    asyncio.run(main())
