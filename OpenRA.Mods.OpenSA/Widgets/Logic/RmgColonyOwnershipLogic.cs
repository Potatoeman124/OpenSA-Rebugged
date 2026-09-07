#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class RmgColonyOwnershipLogic : ChromeLogic
	{
		readonly RmgColonyWeightsLogic editor = new();
		[ObjectCreator.UseCtor]
		public RmgColonyOwnershipLogic(Widget widget, int[] initialShares, Func<bool> configurationDisabled, Action<int[]> onApply)
		{
			editor.Configure(widget, (int[])initialShares.Clone(), Enumerable.Range(1, initialShares.Length).Select(i => $"Player {i} (lobby slot {i})").ToArray(),
				0, true, configurationDisabled, onApply);
		}
		public override void Tick() => editor.Tick();
	}
}
