using System;

namespace ReforgedNet.LL
{
    public static class RTransactionGenerator
    {
        private static readonly Random _rnd = new Random();

        /// <summary>
        /// Returns random generated transaction id.
        /// You could also use your own implementation.
        /// </summary>
        /// <returns></returns>
        public static long GenerateId()
        {
            byte[] buf = new byte[8];
            _rnd.NextBytes(buf);
            long longRand = BitConverter.ToInt64(buf, 0);
            return (Math.Abs(longRand % long.MaxValue));
        }
    }
}
