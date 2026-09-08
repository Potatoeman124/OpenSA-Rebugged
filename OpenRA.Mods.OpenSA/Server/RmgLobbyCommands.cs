#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion
using System;
using OpenRA.Network;
using OpenRA.Server;

namespace OpenRA.Mods.OpenSA.Server
{
	// Runs before the common lobby commands; leaves normal map selection and start setup to the engine.
	public sealed class RmgLobbyCommands : ServerTrait, IInterpretCommand
	{
		public static bool IsGeneratedMap(MapPreview map) => map?.Author?.StartsWith("OpenSA RMG v", StringComparison.Ordinal) == true;

		public bool InterpretCommand(OpenRA.Server.Server server, Connection conn, Session.Client client, string cmd)
		{
			if (server.State != ServerState.WaitingPlayers || !client.IsAdmin || !IsGeneratedMap(server.Map)) return false;
			// Re-selecting the same UID otherwise invalidates every client without triggering client-side map acknowledgement.
			if (cmd == "map " + server.LobbyInfo.GlobalSettings.Map) return true;
			if (cmd == "startgame" && client.IsInvalid)
			{
				server.SendOrderTo(conn, "Message", "The generated map is still being confirmed. Please wait before starting.");
				return true;
			}
			return false;
		}
	}
}
