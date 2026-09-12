#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Traits.World
{
	[TraitLocation(SystemActors.World)]
	[Desc("Local display of the current weapon ranges of selected, visible actors.")]
	public sealed class SelectedUnitRangeOverlayInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new SelectedUnitRangeOverlay();
	}

	public sealed class SelectedUnitRangeOverlay : IRenderAnnotations
	{
		// Presentation state only: never synchronized and never changes weapon or order logic.
		SelectionRange[] previousRanges = Array.Empty<SelectionRange>();
		MergedRangeAnnotationRenderable cachedOutline;

		public bool Enabled { get; private set; }
		public void Toggle() => Enabled = !Enabled;

		public IEnumerable<IRenderable> RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			var ranges = GetRanges(self.World).ToArray();
			if (ranges.Length == 0)
				yield break;

			if (!ranges.SequenceEqual(previousRanges))
			{
				previousRanges = ranges;
				cachedOutline = new MergedRangeAnnotationRenderable(ranges);
			}

			yield return cachedOutline;
		}

		public static IEnumerable<SelectionRange> GetRanges(OpenRA.World world)
		{
			foreach (var actor in world.Selection.Actors)
			{
				if (!actor.IsInWorld || actor.IsDead || actor.Disposed || world.FogObscures(actor))
					continue;

				var range = WDist.Zero;
				foreach (var attack in actor.TraitsImplementing<AttackBase>())
				{
					var candidate = attack.GetMaximumRange();
					if (candidate > range)
						range = candidate;
				}

				if (range > WDist.Zero)
					yield return new SelectionRange(actor.CenterPosition, range, Color.FromArgb(210, actor.Owner.Color));
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
