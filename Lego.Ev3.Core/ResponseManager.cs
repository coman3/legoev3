using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lego.Ev3.Core
{
	internal class ResponseManager
	{
        private object _lockObj = new object();
        private ushort _nextSequence = 0x0001;
        private readonly Dictionary<int, TaskCompletionSource<Response>> Responses = new Dictionary<int, TaskCompletionSource<Response>>();

        private ushort GetSequenceNumber()
        {
            if (_nextSequence == UInt16.MaxValue)
                _nextSequence++;

            return _nextSequence++;
        }

        internal ushort RegisterSequenceNumber()
        {
            lock (_lockObj)
            {
                ushort sequence = GetSequenceNumber();
                Responses.Add(sequence, new TaskCompletionSource<Core.Response>());
                return sequence;
            }
        }

        internal async Task<Response> WaitForResponseAsync(ushort sequnceNo)
        {
            TaskCompletionSource<Response> waiter;

            lock (_lockObj)
            {
                if (!Responses.TryGetValue(sequnceNo, out waiter))
                    throw new ArgumentException("Sequence number " + sequnceNo + " is not registered!");
            }

            await Task.WhenAny(waiter.Task, Task.Delay(10000));

            lock (_lockObj)
            {
                if (!waiter.Task.IsCompleted)
                    return new Response(sequnceNo) { ReplyType = ReplyType.DirectReplyError };
                else
                    return waiter.Task.Result;
            }
        }

		internal void HandleResponse(byte[] report)
		{
			if (report == null || report.Length < 3)
				return;

			ushort sequence = (ushort) (report[0] | (report[1] << 8));
			int replyType = report[2];

			//System.Diagnostics.Debug.WriteLine("Size: " + report.Length + ", Sequence: " + sequence + ", Type: " + (ReplyType)replyType + ", Report: " + BitConverter.ToString(report));

			if (sequence > 0)
			{
                Response r = new Response(sequence);

				if (Enum.IsDefined(typeof (ReplyType), replyType))
					r.ReplyType = (ReplyType) replyType;

				if (r.ReplyType == ReplyType.DirectReply || r.ReplyType == ReplyType.DirectReplyError)
				{
					r.Data = new byte[report.Length - 3];
					Array.Copy(report, 3, r.Data, 0, report.Length - 3);
				}
				else if (r.ReplyType == ReplyType.SystemReply || r.ReplyType == ReplyType.SystemReplyError)
				{
					if (Enum.IsDefined(typeof (SystemOpcode), (int) report[3]))
						r.SystemCommand = (SystemOpcode) report[3];

					if (Enum.IsDefined(typeof (SystemReplyStatus), (int) report[4]))
						r.SystemReplyStatus = (SystemReplyStatus) report[4];

					r.Data = new byte[report.Length - 5];
					Array.Copy(report, 5, r.Data, 0, report.Length - 5);
				}

                TaskCompletionSource<Response> waitSrc;
                lock (_lockObj) Responses.TryGetValue(sequence, out waitSrc);
                if (waitSrc != null)
                    waitSrc.SetResult(r);
			}
		}
	}
}
