import asyncio, struct
from bleak import BleakClient

async def main():
    async with BleakClient('D8:81:2F:0B:F2:7F', timeout=20) as client:
        dpg = '99fa0011-338a-1024-8a49-009c0215f78a'
        queue = asyncio.Queue()
        await client.start_notify(dpg, lambda _, data: queue.put_nowait(bytes(data)))
        await client.write_gatt_char(dpg, bytes.fromhex('7f8100'), response=True)
        reply = await asyncio.wait_for(queue.get(), 5)
        print('Offset response:', reply.hex())
        if len(reply) != 5 or reply[:2] != bytes.fromhex('0103'):
            raise RuntimeError('Unexpected offset response')
        offset = struct.unpack('<H', reply[3:5])[0]
        for sample in range(3):
            value = bytes(await client.read_gatt_char('99fa0021-338a-1024-8a49-009c0215f78a'))
            raw, speed = struct.unpack('<Hh', value)
            print(f'Sample {sample+1}: raw={value.hex()}, extension_mm={raw/10:.1f}, base_mm={offset/10:.1f}, height_cm={(offset+raw)/100:.2f}, speed_raw={speed}', flush=True)
            await asyncio.sleep(0.5)
        await client.stop_notify(dpg)

asyncio.run(main())
