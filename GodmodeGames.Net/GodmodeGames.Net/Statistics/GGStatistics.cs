using System;

namespace GodmodeGames.Net.Transport.Statistics
{
    public class GGStatistics
    {
        // <summary>
        /// Total amount of sent bytes
        /// </summary>
        public long TotalBytesSent = 0;
        /// <summary>
        /// Total amount of received bytes
        /// </summary>
        public long TotalByteReceived = 0;
        /// <summary>
        /// Amount of bytes sent in last second
        /// </summary>
        public long TotalBytesSentLastSecond = 0;
        private long TotalBytesSentCurrentSecond = 0;
        private int CurrentSecondSend = 0;
        /// <summary>
        /// Amount of bytes received last second
        /// </summary>
        public long TotalBytesReceivedLastSecond = 0;
        private long TotalBytesReceivedCurrentSecond = 0;
        private int CurrentSecondReceived = 0;
        /// <summary>
        /// Datetime of last received data
        /// </summary>
        public DateTime LastDataReceived = DateTime.Now;

        /// <summary>
        /// Number of packets sent
        /// </summary>
        public long TotalPacketsSent = 0;
        /// <summary>
        /// Number of packets received
        /// </summary>
        public long TotalPacketReceived = 0;
        /// <summary>
        /// Packets lost
        /// </summary>
        public long TotalPacketsLost = 0;
        /// <summary>
        /// How many packets are lost last second
        /// </summary>
        public long PacketsLostLastSecond = 0;
        private long PacketsLostCurrentSecond = 0;
        private int CurrentSecondPacketLost = 0;

        /// <summary>
        /// Update Receive Statistics
        /// </summary>
        /// <param name="numOfSentBytes"></param>
        internal void UpdateReceiveStatistics(int numOfReceivedBytes)
        {
            //Statisktiken
            this.LastDataReceived = DateTime.Now;
            this.TotalByteReceived += numOfReceivedBytes;
            this.TotalPacketReceived++;

            int curr = DateTime.Now.Second;
            if (curr == this.CurrentSecondReceived)
            {
                this.TotalBytesReceivedCurrentSecond += numOfReceivedBytes;
            }
            else
            {
                this.TotalBytesReceivedLastSecond = this.TotalBytesReceivedCurrentSecond;
                this.TotalBytesReceivedCurrentSecond = numOfReceivedBytes;
                this.CurrentSecondReceived = curr;
            }
        }

        /// <summary>
        /// Update Sent-Statistics
        /// </summary>
        /// <param name="numOfSentBytes"></param>
        internal void UpdateSentStatistics(int numOfSentBytes)
        {
            this.TotalBytesSent += numOfSentBytes;
            this.TotalPacketsSent++;

            int curr = DateTime.Now.Second;
            if (curr == this.CurrentSecondSend)
            {
                this.TotalBytesSentCurrentSecond += numOfSentBytes;
            }
            else
            {
                this.TotalBytesSentLastSecond = this.TotalBytesSentCurrentSecond;
                this.TotalBytesSentCurrentSecond = numOfSentBytes;
                this.CurrentSecondSend = curr;
            }
        }

        /// <summary>
        /// Update Packet-Lost statistics
        /// </summary>
        internal void UpdatePacketLost()
        {
            this.TotalPacketsLost++;
            int curr = DateTime.Now.Second;
            if (curr == this.CurrentSecondPacketLost)
            {
                this.PacketsLostCurrentSecond++;
            }
            else
            {
                this.PacketsLostLastSecond = this.PacketsLostCurrentSecond;
                this.PacketsLostCurrentSecond = 1;
                this.CurrentSecondPacketLost = curr;
            }
        }
    }
}
