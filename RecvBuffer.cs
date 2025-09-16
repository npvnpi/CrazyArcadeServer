using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrazyArcadeServer
{
    internal class RecvBuffer
    {
        private const int Chunk = 4096;
        private const int Capacity = Chunk * 5;

        public byte[] MyBuffer {  get; set; } = new byte[Capacity];
        public int WritePos { get; set; } = 0;
        public int ReadPos { get; set; } = 0;

        public int DataLength => WritePos - ReadPos;
        public int FreeSpace => Capacity - WritePos;

        // [x][1/r][2][3][w]
        public void Read(int numOfBytes) 
        {
            if (numOfBytes < 0 || numOfBytes > DataLength)
                throw new ArgumentOutOfRangeException(nameof(numOfBytes));

            // 데이터 읽기
            ReadPos += numOfBytes;
            // 읽다가 ReadPos랑 WritePos가 같아지면 0번 오프셋으로 이동
            if (ReadPos == WritePos) 
            {
                ReadPos = 0;
                WritePos = 0;
            }
        }

        public void AdvanceWrite(int numOfBytes)
        {
            if (numOfBytes < 0 || numOfBytes > FreeSpace)
                throw new ArgumentOutOfRangeException(nameof(numOfBytes));

            WritePos += numOfBytes;

            // 여유가 너무 줄어들면 앞으로 당겨서 공간 확보
            if (WritePos >= Capacity - Chunk) // 4번째 청크 근처
                Compact();
        }

        // 소켓 Recv 받을 때 이 세그먼트로 바로 넣기
        public ArraySegment<byte> GetWritableSegment()
            => new ArraySegment<byte>(MyBuffer, WritePos, FreeSpace);

        // 파싱할 때 읽을 수 있는 바이트 범위
        public ArraySegment<byte> GetReadableSegment()
            => new ArraySegment<byte>(MyBuffer, ReadPos, DataLength);

        // 외부 버퍼에서 데이터 복사해서 넣고 싶을 때
        public void WriteBytes(byte[] src, int offset, int count)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if ((uint)offset > src.Length || (uint)count > src.Length - offset)
                throw new ArgumentOutOfRangeException();

            EnsureFree(count);
            Buffer.BlockCopy(src, offset, MyBuffer, WritePos, count);
            WritePos += count;

            if (WritePos >= Capacity - Chunk)
                Compact();
        }

        // 바이트를 꺼내서 새 배열로 반환 (필요하면)
        public byte[] ReadBytes(int count)
        {
            if (count < 0 || count > DataLength)
                throw new ArgumentOutOfRangeException(nameof(count));

            byte[] dst = new byte[count];
            Buffer.BlockCopy(MyBuffer, ReadPos, dst, 0, count);
            Read(count);
            return dst;
        }


        private void Compact()
        {
            int len = DataLength;
            if (len > 0)
            {
                Buffer.BlockCopy(MyBuffer, ReadPos, MyBuffer, 0, len);
                ReadPos = 0;
                WritePos = len;
            }
            else
            {
                ReadPos = 0;
                WritePos = 0;
            }
        }

        private void EnsureFree(int needed)
        {
            if (needed <= FreeSpace) return;

            // 컴팩트로 해결 가능하면 우선 시도
            Compact();

            if (needed > FreeSpace)
                throw new InvalidOperationException("RecvBuffer capacity exceeded.");
        }
    }
}
