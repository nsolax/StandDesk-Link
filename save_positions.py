"""Save the two measured positions without issuing any movement commands."""
import asyncio
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

from bleak import BleakClient

ADDRESS = 'D8:81:2F:0B:F2:7F'
DPG = '99fa0011-338a-1024-8a49-009c0215f78a'
HEIGHT = '99fa0021-338a-1024-8a49-009c0215f78a'
TARGETS = {1: 1162, 2: 5437}


async def main():
    log = {'utc': datetime.now(timezone.utc).isoformat(), 'before': {}, 'after': {}}
    path = Path('backups') / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    path.mkdir(parents=True, exist_ok=False)

    def record():
        (path / 'preset-change.json').write_text(json.dumps(log, indent=2), encoding='utf-8')

    async def session(client, verify_only=False):
        queue = asyncio.Queue()
        await client.start_notify(DPG, lambda _, data: queue.put_nowait(bytes(data)))

        async def command(cmd, payload=None):
            while not queue.empty():
                queue.get_nowait()
            request = bytes([127, cmd, 0 if payload is None else 128])
            if payload is not None:
                request += payload
            await client.write_gatt_char(DPG, request, response=True)
            reply = await asyncio.wait_for(queue.get(), 5)
            if len(reply) < 2 or reply[0] != 1:
                raise RuntimeError(f'Command {cmd}: rejected response {reply.hex()}')
            return reply

        if not verify_only:
            for slot in (1, 2, 3):
                log['before'][str(slot)] = (await command(136 + slot)).hex()
            user_reply = await command(134)
            log['user_id_before'] = user_reply.hex()
            record()  # Baseline is durable before any persistent write.
            for _ in range(3):
                raw, speed = struct.unpack('<Hh', bytes(await client.read_gatt_char(HEIGHT)))
                if speed != 0 or raw != TARGETS[2]:
                    raise RuntimeError('Standing position changed or desk is moving; no presets written')
                await asyncio.sleep(0.2)
            user_id = bytearray(user_reply[2:])
            if len(user_id) != 17:
                raise RuntimeError('Unexpected user identifier length')
            if user_id[0] != 1:
                user_id[0] = 1  # Owner mode, as used by linak-controller.
                log['owner_mode_response'] = (await command(134, user_id)).hex()
                record()
                if log['owner_mode_response'] != '0100':
                    raise RuntimeError('Owner mode was not acknowledged')
            for slot, raw in TARGETS.items():
                payload = b'\x01' + struct.pack('<H', raw) + b'\x00' * 4
                response = await command(136 + slot, payload)
                log[f'write_{slot}_response'] = response.hex()
                record()
                if response != b'\x01\x00':
                    raise RuntimeError('Unexpected write acknowledgement; stopping')
        for slot, raw in TARGETS.items():
            reply = await command(136 + slot)
            log['after'][str(slot)] = reply.hex()
            record()
            if len(reply) != 9 or reply[2:5] != b'\x01' + struct.pack('<H', raw):
                raise RuntimeError(f'Preset {slot} verification failed: {reply.hex()}')
        third = (await command(139)).hex()
        if third != log['before']['3']:
            raise RuntimeError('Third preset unexpectedly changed')
        await client.stop_notify(DPG)

    try:
        async with BleakClient(ADDRESS, timeout=20) as client:
            await session(client)
        async with BleakClient(ADDRESS, timeout=20) as client:
            await session(client, verify_only=True)
        log['verified_after_reconnect'] = True
        desired = json.loads(Path('desired_positions.json').read_text(encoding='utf-8-sig'))
        desired['standing'] = {'reported_height_cm': 116.87, 'extension_raw': 5437,
                               'configured_base_raw': 6250, 'verified_stationary_samples': 3,
                               'source': 'Bluetooth height plus configured desk offset',
                               'physically_measured': False}
        desired['written_to_desk'] = True
        desired['slots'] = {'1': 'sitting', '2': 'standing'}
        desired['verified_after_reconnect'] = True
        Path('desired_positions.json').write_text(json.dumps(desired, indent=2), encoding='utf-8')
        print('Saved and verified after reconnect: sitting 74.12 cm; standing 116.87 cm')
    except Exception as exc:
        log['error'] = str(exc)
        raise
    finally:
        record()
        print('Change log:', (path / 'preset-change.json').resolve())


if __name__ == '__main__':
    asyncio.run(main())
