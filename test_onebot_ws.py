import asyncio
import websockets
import json

async def test_ws():
    uri = "ws://127.0.0.1:3001"
    print(f"Connecting to {uri}...")
    try:
        async with websockets.connect(uri) as websocket:
            print("Successfully connected!")
            
            # 发送私聊消息给用户
            payload = {
                "action": "send_private_msg",
                "params": {
                    "user_id": 1330958515,
                    "message": "你好！这是来自 Antigravity 的测试消息。我成功连接到了你的 OneBot WebSocket 服务器！"
                },
                "echo": "msg_test"
            }
            print(f"Sending request: {json.dumps(payload, ensure_ascii=False)}")
            await websocket.send(json.dumps(payload))
            
            # 持续接收消息直到拿到响应
            while True:
                response_str = await asyncio.wait_for(websocket.recv(), timeout=5.0)
                response = json.loads(response_str)
                print(f"Received message: {response_str}")
                
                # 检查是否是我们的 API 响应
                if response.get("echo") == "msg_test":
                    print("Successfully received message confirmation!")
                    break
                else:
                    print("Received a non-echo message (likely an event), waiting for next...")
            
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    try:
        import websockets
    except ImportError:
        print("Error: 'websockets' library not found. Please run 'pip install websockets'")
    else:
        asyncio.run(test_ws())
