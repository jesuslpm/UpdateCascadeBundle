using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Json.Linq;

namespace Raven.Bundles.UpdateCascade
{
	public static class RavenJObjectExtensions
	{
		public static IEnumerable<RavenJObject> GetObjectsAtPath(this RavenJObject obj, string path)
		{
			var properties = path.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
			var currentLevel = Enumerable.Repeat(obj, 1);
			foreach (var property in properties)
			{
				currentLevel = GetNextLevel(currentLevel, property);
			}
			return currentLevel;
		}

		private static IEnumerable<RavenJObject> GetNextLevel(IEnumerable<RavenJObject> objects, string propertyName)
		{
			foreach (var obj in objects)
			{
				RavenJToken token;
				if (obj.TryGetValue(propertyName, out token))
				{
					var jobject = token as RavenJObject;
					if (jobject != null)
					{
						yield return jobject;
					}

					var jarray = token as RavenJArray;
					if (jarray != null)
					{
						foreach (var item in jarray.OfType<RavenJObject>())
						{
							yield return item;
						}
					}
				}
			}
		}
	}
}
