using System;

namespace GodmodeGames.Net.Transport
{
    internal abstract class Message
    {
        /// <summary>
        /// Byte-Array
        /// </summary>
        internal byte[] Data = new byte[0];

        /// <summary>
        /// Execute message (for ping-simulation)
        /// </summary>
        internal DateTime ExecuteTime = DateTime.UtcNow;
        /// <summary>
        /// is the simulated ping already set
        /// </summary>
        internal bool SimulatedPingAdded = false;

        /// <summary>
        /// serialise the byte-array to be send
        /// </summary>
        /// <returns></returns>
        internal abstract byte[] Serialize();

        /// <summary>
        /// Adds a simulated ping
        /// </summary>
        /// <param name="ping"></param>
        internal void SetPing(int ping)
        {
            if (this.SimulatedPingAdded == true)
            {
                return;
            }

            if (ping > 0)
            {
                this.SimulatedPingAdded = true;
                this.ExecuteTime = DateTime.UtcNow.AddMilliseconds(ping);
            }
            else
            {
                this.ExecuteTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(1));
            }
        }
    }
}
