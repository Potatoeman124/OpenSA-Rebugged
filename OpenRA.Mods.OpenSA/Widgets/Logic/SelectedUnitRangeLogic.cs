#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class SelectedUnitRangeLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public SelectedUnitRangeLogic(Widget widget, World world)
		{
			var overlay = world.WorldActor.Trait<SelectedUnitRangeOverlay>();
			var button = widget.Get<ButtonWidget>("SELECTED_UNIT_RANGE");
			button.OnClick = overlay.Toggle;
			button.IsHighlighted = () => overlay.Enabled;
			widget.Get<SelectedUnitRangeIconWidget>("RANGE_ICON").IsActive = () => overlay.Enabled;
		}
	}
}
