#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets
{
	// This transparent listener sits above the player controls, observes input, and never consumes it.
	// A bare Alt tap toggles the overlay on release; Alt commands retain their existing meaning.
	public sealed class SelectedUnitRangeHotkeyWidget : Widget
	{
		readonly SelectedUnitRangeOverlay overlay;
		Keycode? pendingAlt;

		[ObjectCreator.UseCtor]
		public SelectedUnitRangeHotkeyWidget(World world)
		{
			overlay = world.WorldActor.Trait<SelectedUnitRangeOverlay>();
			IgnoreMouseOver = true;
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (e.Key is Keycode.LALT or Keycode.RALT)
			{
				if (e.Event == KeyInputEvent.Down && !e.IsRepeat)
					pendingAlt = pendingAlt == null && (e.Modifiers & ~Modifiers.Alt) == Modifiers.None &&
						Ui.KeyboardFocusWidget == null && Ui.MouseFocusWidget == null ? e.Key : null;
				else if (e.Event == KeyInputEvent.Up)
				{
					if (pendingAlt == e.Key && (e.Modifiers & ~Modifiers.Alt) == Modifiers.None &&
						Ui.KeyboardFocusWidget == null && Ui.MouseFocusWidget == null && Game.Renderer.WindowHasInputFocus)
						overlay.Toggle();
					pendingAlt = null;
				}
			}
			else
				pendingAlt = null;

			return false;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Event != MouseInputEvent.Move || mi.Button != MouseButton.None)
				pendingAlt = null;
			return false;
		}

		public override void Tick()
		{
			if (!Game.Renderer.WindowHasInputFocus || Ui.KeyboardFocusWidget != null || Ui.MouseFocusWidget != null)
				pendingAlt = null;
		}

		public override void Hidden() => pendingAlt = null;
	}
}
