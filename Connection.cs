using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CrazyArcadeServer
{
    internal class Connection
    {
        private readonly Socket _socket;

        // Recv
        private readonly SocketAsyncEventArgs _recvArgs;
        private RecvBuffer _recvBuffer = new RecvBuffer();

        // Send
        private readonly SocketAsyncEventArgs _sendArgs;
        private readonly Queue<ArraySegment<byte>> _sendQ = new();
        private readonly object _sendLock = new();
        private bool _sending;
        private ArraySegment<byte>? _sendingSeg;
        private int _sendingOffset; // seg.Offset 기준 상대 오프셋

        public Connection(Socket socket)
        {
            _socket = socket;

            // --- Recv SAEA ---
            _recvArgs = new SocketAsyncEventArgs();
            var writable = _recvBuffer.GetWritableSegment();
            _recvArgs.SetBuffer(writable.Array, writable.Offset, writable.Count);
            _recvArgs.UserToken = this;
            _recvArgs.Completed += RecvCompleted;

            // --- Send SAEA ---
            _sendArgs = new SocketAsyncEventArgs();
            _sendArgs.UserToken = this;
            _sendArgs.Completed += SendCompleted;
        }

        public void Send(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0) return;
            var copy = new byte[data.Length];
            data.CopyTo(copy);
            EnqueueSend(copy);
        }


        public void StartReceive()
        {
            try
            {
                bool pending = _socket.ReceiveAsync(_recvArgs);
                if (!pending) RecvCompleted(null, _recvArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RECV-START-EX] {ex}");
                Close();
            }
        }

        private void RecvCompleted(object sender, SocketAsyncEventArgs e) 
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                // 클라이언트 종료 혹은 에러
                Console.WriteLine($"[RECV-END] {_socket.RemoteEndPoint} {e.SocketError}");
                Close();
                return;
            }

            try
            {
                _recvBuffer.AdvanceWrite(e.BytesTransferred);  // ✅ WritePos 전진

                ParsePackets();

                var writable = _recvBuffer.GetWritableSegment();
                _recvArgs.SetBuffer(writable.Array, writable.Offset, writable.Count);

                // 다음 수신
                bool pending = _socket.ReceiveAsync(_recvArgs);
                if (!pending) RecvCompleted(null, _recvArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RECV-EX] {ex}");
                Close();
            }
        }

        private void ParsePackets()
        {
            while (true)
            {
                var readable = _recvBuffer.GetReadableSegment();
                if (readable.Count < 2) break; // 헤더 미도착

                ushort len = BitConverter.ToUInt16(readable.Array!, readable.Offset);
                if (readable.Count < len) break; // 패킷 전체 미도착

                // 패킷 처리: readable.Array![readable.Offset .. readable.Offset+len]
                // HandlePacket(new ArraySegment<byte>(readable.Array!, readable.Offset, len));

                _recvBuffer.Read(len); // ✅ 소비
            }
        }


        // 안전한 비동기 전송: 큐에 넣고, 현재 전송 중이 아니면 시작
        private void EnqueueSend(byte[] data)
        {
            lock (_sendLock)
            {
                _sendQ.Enqueue(new ArraySegment<byte>(data, 0, data.Length));
                if (!_sending)
                {
                    _sending = true;
                    TryDequeueAndSend();
                }
            }
        }

        private void TryDequeueAndSend()
        {
            ArraySegment<byte> seg;
            int offset, remain;

            lock (_sendLock)
            {
                while (true)
                {
                    if (_sendingSeg == null)
                    {
                        if (_sendQ.Count == 0) { _sending = false; return; }
                        _sendingSeg = _sendQ.Dequeue();
                        _sendingOffset = 0;
                    }

                    seg = _sendingSeg.Value;
                    offset = seg.Offset + _sendingOffset;
                    remain = seg.Count - _sendingOffset;

                    if (remain > 0) break; // ✅ 보낼 게 있을 때만 탈출

                    // 남은 게 없으면 다음 세그먼트 탐색
                    _sendingSeg = null;
                    _sendingOffset = 0;
                }

                _sendArgs.SetBuffer(seg.Array!, offset, remain);
            }

            try
            {
                bool pending = _socket.SendAsync(_sendArgs);
                if (!pending) SendCompleted(null, _sendArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEND-START-EX] {ex}");
                Close();
            }
        }


        private void SendCompleted(object? sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                Console.WriteLine($"[SEND-ERR] {_socket.RemoteEndPoint} {e.SocketError}");
                Close();
                return;
            }

            lock (_sendLock)
            {
                _sendingOffset += e.BytesTransferred;
                var seg = _sendingSeg!.Value;

                if (_sendingOffset >= seg.Count)
                {
                    // 현 항목 완료 → 다음 항목 준비
                    _sendingSeg = null;
                    _sendingOffset = 0;
                }
            }

            // 이어서 계속
            TryDequeueAndSend();
        }

        private int _closed;
        private void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
            _recvArgs.Completed -= RecvCompleted;
            _sendArgs.Completed -= SendCompleted;
            _recvArgs.Dispose();
            _sendArgs.Dispose();
        }
    }
}
