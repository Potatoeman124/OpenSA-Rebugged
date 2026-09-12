#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
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
		public bool Enabled { get; private set; }
		public void Toggle() => Enabled = !Enabled;

		public IEnumerable<IRenderable> RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			foreach (var actor in self.World.Selection.Actors)
			{
				if (!actor.IsInWorld || actor.IsDead || actor.Disposed || self.World.FogObscures(actor))
					continue;

				var range = WDist.Zero;
				foreach (var attack in actor.TraitsImplementing<AttackBase>())
				{
					var candidate = attack.GetMaximumRange();
					if (candidate > range)
						range = candidate;
				}

				if (range > WDist.Zero)
					yield return new RangeCircleAnnotationRenderable(actor.CenterPosition, range, 0,
						Color.FromArgb(210, actor.Owner.Color), 1.5f, Color.FromArgb(150, Color.Black), 3.5f);
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
