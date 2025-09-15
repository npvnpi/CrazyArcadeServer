using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrazyArcadeServer
{
    internal class RecvBuffer
    {
        public byte[] MyBuffer {  get; set; } = new byte[4096 * 5];
        public int WritePos { get; set; } = 0;
        public int ReadPos { get; set; } = 0;

        public int DataLength()
        {
            return WritePos - ReadPos;
        }

        // [x][1/r][2][3][w]
        public void Read(int numOfBytes) 
        {
            // 데이터 읽기
            ReadPos += numOfBytes;
            // 읽다가 ReadPos랑 WritePos가 같아지면 0번 오프셋으로 이동
            if (ReadPos == WritePos) 
            {
                ReadPos = 0;
                WritePos = 0;
            }
        }

        public void Write(int numOfBytes) 
        {
            WritePos += numOfBytes;

            // 4번째 청크까지 왔다는것은 이제 버퍼 Overflow가 얼마 안남았다는거...
            if (WritePos >= 4096 * 4) 
            {
                int dataSize = DataLength();
                // Buffer ReadPos부터 WritePos까지의 부분을 o부터 DataSize까지 복사해줘야하는데 C#에서 어떻게해?
                Buffer.BlockCopy(MyBuffer, ReadPos, MyBuffer, 0, dataSize);

                ReadPos = 0;
                WritePos = dataSize;
            }

        }
    }
}
