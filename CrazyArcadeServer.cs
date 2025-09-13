using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CrazyArcadeServer
{
    internal class CrazyArcadeServer
    {
        private Socket _listenSocket { get; set; }
        private readonly int _acceptParallel;    // 동시에 걸어둘 Accept 개수
        private readonly int _backlog;

        public CrazyArcadeServer(int acceptParallel = 8, int backlog = 512)
        {
            _acceptParallel = Math.Max(1, acceptParallel);
            _backlog = Math.Max(100, backlog);
        }

        public void Start(int port) 
        {
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _listenSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            _listenSocket.Listen(_backlog);

            Console.WriteLine($"서버 시작! 포트: {port}");

            // 동시에 여러 AcceptAsync를 걸어둔다
            for (int i = 0; i < _acceptParallel; i++)
                StartAccept();
        }

        private void StartAccept()
        {
            var args = new SocketAsyncEventArgs();
            args.Completed += AcceptCompleted;
            bool pending = _listenSocket.AcceptAsync(args);
            if (!pending) AcceptCompleted(null, args);
        }

        private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    var client = e.AcceptSocket;
                    Console.WriteLine($"[ACCEPT] {client.RemoteEndPoint}");

                    // 연결별 세션 생성
                    var conn = new Connection(client);
                    conn.StartReceive();
                }
                else
                {
                    // 실패했어도 Accept 루프는 계속!
                    Console.WriteLine($"[ACCEPT-ERR] {e.SocketError}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ACCEPT-EX] {ex}");
            }
            finally
            {
                // 재사용을 위해 정리하고 다음 Accept
                e.AcceptSocket = null;
                e.Completed -= AcceptCompleted;
                e.Dispose();
                StartAccept();
            }
        }
    }
}
