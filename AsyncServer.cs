using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CrazyArcadeServer
{
    internal class AsyncServer
    {
        private Socket _listener;

        public void Start(int port) 
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);    

            _listener.Bind(new IPEndPoint(IPAddress.Any, port));    
            _listener.Listen(100);

            Console.WriteLine($"서버 시작! 포트: {port}");

            StartAccept();
        }


        private void StartAccept() 
        {
            var args = new SocketAsyncEventArgs();
            args.Completed += AcceptCompleted;

            bool pending = _listener.AcceptAsync(args);
            if (!pending) 
            {
                AcceptCompleted(null, args);
            }
        }

        private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                Socket client = e.AcceptSocket;
                Console.WriteLine($"클라이언트 접속! {client.RemoteEndPoint}");

                StartReceive(client);
            }

            // 다음 Accept 준비
            e.AcceptSocket = null;
            StartAccept();
        }

        private void StartReceive(Socket client)
        {
            var buffer = new byte[1024];
            var args = new SocketAsyncEventArgs();
            args.SetBuffer(buffer, 0, buffer.Length);
            args.UserToken = client;
            args.Completed += IOCompleted;

            bool pending = client.ReceiveAsync(args);
            if (!pending)
            {
                IOCompleted(null, args);
            }
        }

        private void IOCompleted(object sender, SocketAsyncEventArgs e)
        {
            var client = (Socket)e.UserToken;

            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                string msg = System.Text.Encoding.UTF8.GetString(e.Buffer, 0, e.BytesTransferred);
                Console.WriteLine($"받음: {msg}");

                // 다시 받기
                bool pending = client.ReceiveAsync(e);
                if (!pending)
                    IOCompleted(null, e);
            }
            else
            {
                Console.WriteLine("클라이언트 종료됨");
                client.Close();
            }
        }

    }
}
