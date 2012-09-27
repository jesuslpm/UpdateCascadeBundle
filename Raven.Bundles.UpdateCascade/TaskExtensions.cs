using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


namespace Raven.Bundles.UpdateCascade
{
	internal static class TaskExtensions
	{
		public static Task Delay(TimeSpan time)
		{
			var cs = new TaskCompletionSource<bool>();
			Timer t = new Timer(self =>
			{
				((Timer)self).Dispose();
				cs.TrySetResult(true);
			});
			t.Change(time, TimeSpan.FromMilliseconds(-1.0));
			return cs.Task;
		}

		public static Task Delay(TimeSpan time, CancellationToken token)
		{
			var cs = new TaskCompletionSource<bool>();
			Timer timer = new Timer(self =>
			{
				((Timer)self).Dispose();
				cs.SetResult(true);
			});
			timer.Change(time, TimeSpan.FromMilliseconds(-1.0));
			token.Register(() =>
			{
				timer.Dispose();				
				cs.TrySetCanceled();
			});
			return cs.Task;
		}
	}
}
