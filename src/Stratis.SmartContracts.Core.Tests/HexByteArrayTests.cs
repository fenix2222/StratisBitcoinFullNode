using System;
using System.Text;
using Xunit;

namespace Stratis.SmartContracts.Core.Tests
{
    public class HexByteArrayTests
    {
        [Fact]
        public void HexString_ToByteArray_AndBack()
        {
            string hex = "08AFBC56";
            byte[] bytes = hex.HexToByteArray();
            Assert.Equal(4, bytes.Length);
            Assert.Equal(0x08, bytes[0]);
            Assert.Equal(0xAF, bytes[1]);
            Assert.Equal(0xBC, bytes[2]);
            Assert.Equal(0x56, bytes[3]);
            string hexFromByteArray = bytes.ToHexFromByteArray();
            Assert.Equal(hex, hexFromByteArray);
        }

        [Fact]
        public void HexString_ToByteArray_AndBack_LowerCase()
        {
            string hex = "08afbc56";
            byte[] bytes = hex.HexToByteArray();
            Assert.Equal(4, bytes.Length);
            Assert.Equal(0x08, bytes[0]);
            Assert.Equal(0xAF, bytes[1]);
            Assert.Equal(0xBC, bytes[2]);
            Assert.Equal(0x56, bytes[3]);
            string hexFromByteArray = bytes.ToHexFromByteArray();
            // Comes back identical but always upper case
            Assert.Equal(hex.ToUpper(), hexFromByteArray);
        }

        [Fact]
        public void HexString_ToByteArray_AndBack_With0x()
        {
            string hex = "0x08AFBC56";
            byte[] bytes = hex.HexToByteArray();
            Assert.Equal(4, bytes.Length);
            Assert.Equal(0x08, bytes[0]);
            Assert.Equal(0xAF, bytes[1]);
            Assert.Equal(0xBC, bytes[2]);
            Assert.Equal(0x56, bytes[3]);
            string hexFromByteArray = bytes.ToHexFromByteArray();
            // Comes back identical but with 0x
            Assert.Equal(hex, "0x" + hexFromByteArray);
        }

        [Fact]
        public void HexString_ToByteArray_InvalidFails()
        {
            string hex = "08afbc567"; // Uneven number of chars.
            Assert.ThrowsAny<Exception>(() => hex.ToByteArrayFromHex());
        }
    }

    public static class BitExtensions
    {
        public static string ToHexFromByteArray(this byte[] Bytes)
        {
            StringBuilder result = new StringBuilder(Bytes.Length * 2);
            string hexAlphabet = "0123456789ABCDEF";

            foreach (byte B in Bytes)
            {
                result.Append(hexAlphabet[(int)(B >> 4)]);
                result.Append(hexAlphabet[(int)(B & 0xF)]);
            }

            return result.ToString();
        }

        public static byte[] ToByteArrayFromHex(this string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            int[] hexValue = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };

            for (int x = 0, i = 0; i < hex.Length; i += 2, x += 1)
            {
                bytes[x] = (byte)(hexValue[char.ToUpper(hex[i + 0]) - '0'] << 4 |
                                  hexValue[char.ToUpper(hex[i + 1]) - '0']);
            }

            return bytes;
        }
    }
}
