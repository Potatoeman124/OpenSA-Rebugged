#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	// Shared observer logic allows a 400px menu, which can exceed the space on either side of the selector.
	public sealed class ObserverDropdownBoundsLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public ObserverDropdownBoundsLogic(Widget widget)
		{
			var selector = widget.Get<DropDownButtonWidget>("SHROUD_SELECTOR");

			// Run after ObserverShroudSelectorLogic has populated and attached the menu.
			selector.OnMouseDown += _ =>
			{
				var panel = Ui.Root.GetOrNull<ScrollPanelWidget>("SPECTATOR_DROPDOWN_TEMPLATE");
				if (panel == null)
					return;

				var height = Game.Renderer.Resolution.Height;
				if (panel.RenderBounds.Top >= 0 && panel.RenderBounds.Bottom <= height)
					return;

				var anchor = selector.RenderBounds;
				var above = anchor.Top.Clamp(0, height);
				var below = height - anchor.Bottom.Clamp(0, height);
				var openBelow = below >= above;
				panel.Bounds.Height = Math.Min(panel.Bounds.Height, openBelow ? below : above);
				var top = openBelow ? height - below : above - panel.Bounds.Height;
				panel.Bounds.Y = top - panel.Parent.ChildOrigin.Y;
				panel.ScrollToSelectedItem();
			};
		}
	}
}
