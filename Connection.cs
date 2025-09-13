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
        private readonly byte[] _recvBuffer = new byte[4096];

        // Send
        private readonly SocketAsyncEventArgs _sendArgs;
        private readonly Queue<ArraySegment<byte>> _sendQ = new();
        private readonly object _sendLock = new();
        private bool _sending;


        public Connection(Socket socket)
        {
            _socket = socket;

            // --- Recv SAEA ---
            _recvArgs = new SocketAsyncEventArgs();
            _recvArgs.SetBuffer(_recvBuffer, 0, _recvBuffer.Length);
            _recvArgs.UserToken = this;
            _recvArgs.Completed += RecvCompleted;

            // --- Send SAEA ---
            _sendArgs = new SocketAsyncEventArgs();
            _sendArgs.UserToken = this;
            _sendArgs.Completed += SendCompleted;
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
                // 수신 데이터 처리 (예: 에코)
                string text = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
                Console.WriteLine($"[RECV] {_socket.RemoteEndPoint} : {text}");

                // 에코(그대로 돌려보내기)
                byte[] echo = Encoding.UTF8.GetBytes($"echo: {text}");
                EnqueueSend(echo);

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

            lock (_sendLock)
            {
                if (_sendQ.Count == 0)
                {
                    _sending = false;
                    return;
                }
                seg = _sendQ.Peek(); // 보내고 나서 성공 시 Dequeue
            }

            // 부분 전송 대비: _sendArgs.Buffer/Offset/Count를 세팅
            _sendArgs.SetBuffer(seg.Array, seg.Offset, seg.Count);

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

        private void SendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                Console.WriteLine($"[SEND-ERR] {_socket.RemoteEndPoint} {e.SocketError}");
                Close();
                return;
            }

            lock (_sendLock)
            {
                // 부분 전송 처리
                var cur = _sendQ.Peek();
                int sent = e.BytesTransferred;

                if (sent < cur.Count)
                {
                    // 아직 남았다 → 남은 구간으로 다시 세팅하고 재전송
                    var remain = new ArraySegment<byte>(cur.Array!, cur.Offset + sent, cur.Count - sent);
                    _sendQ.Dequeue();      // 기존 항목 제거
                    _sendQ.Enqueue(remain); // 앞에 다시 붙이지 말고, 현재 위치 유지 위해 새로 맨 앞으로 넣는 패턴 대신
                                            // 간단히: 큐를 비우고 remain을 제일 앞으로 넣고 싶다면 다른 자료구조를 쓰자.
                                            // 여기서는 간단화를 위해 아래처럼 '즉시 다시 보냄' 처리.
                                            // 즉시 다시 보내기 위해 큐 정리
                    var tmp = new Queue<ArraySegment<byte>>();
                    tmp.Enqueue(remain);
                    while (_sendQ.Count > 0) tmp.Enqueue(_sendQ.Dequeue());
                    while (tmp.Count > 0) _sendQ.Enqueue(tmp.Dequeue());
                }
                else
                {
                    // 이번 항목 전송 완료
                    _sendQ.Dequeue();
                }
            }

            // 다음 항목 있으면 계속 전송
            TryDequeueAndSend();
        }

        private void Close()
        {
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }

            _recvArgs.Completed -= RecvCompleted;
            _sendArgs.Completed -= SendCompleted;
            _recvArgs.Dispose();
            _sendArgs.Dispose();
        }
    }
}
