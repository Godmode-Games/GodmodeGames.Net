using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public static class RTransactionGenerator
    {
        private static readonly Random _rnd = new Random();

        /// <summary>
        /// Returns random generated transaction id.
        /// You could also use your own implementation.
        /// </summary>
        /// <returns></returns>
        public static int GenerateId()
        {
            // TODO: replace with faster algorithm.
            return _rnd.Next(int.MinValue, int.MaxValue);
        }
    }
}
