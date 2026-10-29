import asyncio
from bleak import BleakClient

async def main():
    async with BleakClient('D8:81:2F:0B:F2:7F', timeout=20.0) as client:
        print('Connected:', client.is_connected, flush=True)
        for service in client.services:
            print('Service', service.uuid, service.description, flush=True)
            for char in service.characteristics:
                print(' Characteristic', char.uuid, char.properties, flush=True)
                if 'read' in char.properties:
                    try:
                        value = await asyncio.wait_for(client.read_gatt_char(char), 5)
                        print('  Value:', bytes(value).hex(), flush=True)
                    except Exception as exc:
                        print('  Read error:', str(exc), flush=True)

asyncio.run(main())
