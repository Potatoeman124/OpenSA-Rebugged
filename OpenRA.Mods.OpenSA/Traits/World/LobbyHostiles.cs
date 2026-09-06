#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Linq;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Traits.World
{
	// Multiplayer-synchronized lobby settings, deliberately independent of the RMG pipeline.
	public sealed class LobbyHostiles : ITick, IPreventMapSpawn
	{
		readonly Actor self;
		readonly PirateSpawnerInfo pirateInfo;
		readonly PlantSpawnerInfo plantInfo;
		readonly CreepFlyerSpawnerInfo flierInfo;
		int pirateTicks;
		int plantTicks;
		int flierTicks;
		bool initialized;
		readonly HostilePopulationBudget pirateBudget = new();
		public bool Active { get; }
		public int ReservedPirates => pirateBudget.Count;

		public LobbyHostiles(Actor self)
		{
			this.self = self;
			var world = self.World;
			Active = world.Type == WorldType.Regular && world.Map.Visibility.HasFlag(MapVisibility.Lobby) &&
				world.LobbyInfo.GlobalSettings.LobbyOptions.ContainsKey("h-initial-count");
			pirateInfo = self.Info.TraitInfoOrDefault<PirateSpawnerInfo>();
			plantInfo = self.Info.TraitInfos<PlantSpawnerInfo>().FirstOrDefault(x => x.Tileset == null || x.Tileset == world.Map.Tileset);
			flierInfo = self.Info.TraitInfos<CreepFlyerSpawnerInfo>().FirstOrDefault(x => x.Tileset == null || x.Tileset == world.Map.Tileset);
		}

		public int Number(string key, int fallback) => HostileOptions.Number(self.World.LobbyInfo.GlobalSettings, key, fallback);
		public string Choose(string group, string[] species) =>
			HostileOptions.Choose(species, HostileOptions.Weights(self.World.LobbyInfo.GlobalSettings, group, self.World.Map.Tileset), self.World.SharedRandom);
		bool HasWeights(string group) => HostileOptions.Weights(self.World.LobbyInfo.GlobalSettings, group, self.World.Map.Tileset).Sum() > 0;
		int Time(string key, int[] fallback) => Number(key, -1) is var seconds && seconds >= 0 ?
			seconds * 25 : Util.RandomInRange(self.World.SharedRandom, fallback);

		public int ReservePirates(int requested)
		{
			return pirateBudget.Reserve(requested, Number("pirate-cap", pirateInfo?.Maximum ?? 0));
		}

		public void ReleasePirates(int count) => pirateBudget.Release(count);

		bool IPreventMapSpawn.PreventMapSpawn(OpenRA.World world, ActorReference reference) =>
			Active && Number("initial-count", -1) >= 0 && HostileOptions.Pirates.Contains(reference.Type.ToLowerInvariant()) &&
			reference.Get<OwnerInit>().InternalName == "Creeps";

		void ITick.Tick(Actor actor)
		{
			if (!Active)
				return;
			if (!initialized)
			{
				initialized = true;
				self.World.AddFrameEndTask(_ => PlaceInitialPirates());
				pirateTicks = Time("pirate-delay", pirateInfo?.InitialSpawnDelay ?? new[] { 1000, 1500 });
				plantTicks = Time("plant-delay", plantInfo?.InitialSpawnDelay ?? new[] { 375, 750 });
				flierTicks = Time("flier-delay", flierInfo?.InitialSpawnDelay ?? new[] { 1000, 1500 });
				return;
			}

			if (self.TraitOrDefault<MapCreeps>()?.Enabled == true && HasWeights("pirate") && --pirateTicks <= 0)
			{
				pirateTicks = Time("pirate-interval", pirateInfo?.SpawnInterval ?? new[] { 1000, 1500 });
				if (pirateBudget.Count < Number("pirate-cap", pirateInfo?.Maximum ?? 0))
				{
					var cell = ChooseGround(pirateInfo?.ValidGround?.ToArray() ?? new[] { "Clear", "Rock", "Vegetation" });
					if (cell.HasValue)
						self.World.AddFrameEndTask(w => w.CreateActor("anthole", new TypeDictionary
						{
							new OwnerInit(w.Players.First(p => p.InternalName == "Creeps")), new LocationInit(cell.Value)
						}));
				}
			}

			if (self.TraitOrDefault<PlantCreeps>()?.Enabled == true && HasWeights("plant") && --plantTicks <= 0)
			{
				plantTicks = Time("plant-interval", plantInfo?.SpawnInterval ?? new[] { 375, 750 });
				// Includes authored plants and seeds, including moth offspring. The cap gates regular spawning, not reproduction.
				if (self.World.ActorsWithTrait<Plant>().Count(x => !x.Actor.IsDead) < Number("plant-cap", plantInfo?.Maximum ?? 0))
				{
					var cell = ChooseGround(plantInfo?.ValidGround?.ToArray() ?? new[] { "Clear", "Rock", "Vegetation" });
					if (cell.HasValue)
					{
						var species = Choose("plant", HostileOptions.Plants);
						self.World.AddFrameEndTask(w => w.CreateActor(species, new TypeDictionary
						{
							new OwnerInit(w.Players.First(p => p.InternalName == "Creeps")), new LocationInit(cell.Value)
						}));
					}
				}
			}

			if (self.TraitOrDefault<FlyerCreeps>()?.Enabled == true && (flierInfo != null || Number("flier-interval", -1) >= 0) && HasWeights("flier") && --flierTicks <= 0)
			{
				flierTicks = Time("flier-interval", flierInfo?.SpawnInterval ?? new[] { 1000, 1500 });
				SpawnFlier();
			}
		}

		CPos? ChooseGround(string[] valid)
		{
			for (var n = 0; n < 100; n++)
			{
				var cell = self.World.Map.ChooseRandomCell(self.World.SharedRandom);
				if (valid.Contains(self.World.Map.GetTerrainInfo(cell).Type) && !self.World.ActorMap.GetActorsAt(cell).Any())
					return cell;
			}

			return null;
		}

		void PlaceInitialPirates()
		{
			var amount = Number("initial-count", -1);
			if (amount <= 0 || !HasWeights("initial"))
				return;
			var world = self.World;
			var owner = world.Players.First(p => p.InternalName == "Creeps");
			var cells = world.Map.AllCells.Where(c => world.Map.Contains(c) && new[] { "Clear", "Rock", "Vegetation" }.Contains(world.Map.GetTerrainInfo(c).Type) &&
				!world.ActorMap.GetActorsAt(c).Any()).ToArray();
			for (var i = cells.Length - 1; i > 0; i--)
			{
				var j = world.SharedRandom.Next(i + 1);
				(cells[i], cells[j]) = (cells[j], cells[i]);
			}

			var distribution = HostileOptions.Value(world.LobbyInfo.GlobalSettings, "h-initial-distribution", "scattered");
			var anchors = cells.Take(Math.Max(1, (amount + 7) / 8)).ToArray();
			for (var n = 0; n < amount; n++)
			{
				var species = Choose("initial", HostileOptions.Pirates);
				var mobile = world.Map.Rules.Actors[species].TraitInfo<MobileInfo>();
				var grouped = distribution == "groups" || (distribution == "mixed" && n % 2 == 0);
				var ordered = cells.AsEnumerable();
				if (grouped && anchors.Length > 0)
				{
					var anchor = anchors[(n / 8) % anchors.Length];
					ordered = ordered.OrderBy(c => Math.Abs(c.X - anchor.X) + Math.Abs(c.Y - anchor.Y));
				}

				var cell = ordered.Where(c => !world.ActorMap.GetActorsAt(c).Any() && mobile.CanEnterCell(world, null, c)).Select(c => (CPos?)c).FirstOrDefault();
				if (!cell.HasValue)
					break;
				world.CreateActor(species, new TypeDictionary { new OwnerInit(owner), new LocationInit(cell.Value) });
			}
		}

		void SpawnFlier()
		{
			var world = self.World;
			var species = Choose("flier", HostileOptions.Fliers);
			var facings = Math.Max(1, flierInfo?.QuantizedFacings ?? 8);
			var facing = new WAngle(world.SharedRandom.Next(facings) * 1024 / facings);
			var delta = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(facing));
			var altitude = world.Map.Rules.Actors[species].TraitInfo<AircraftInfo>().CruiseAltitude.Length;
			var target = world.Map.CenterOfCell(world.Map.ChooseRandomCell(world.SharedRandom)) + new WVec(0, 0, altitude);
			var cordon = flierInfo?.Cordon ?? new WDist(7680);
			var start = target - (world.Map.DistanceToEdge(target, -delta) + cordon).Length * delta / 1024;
			var finish = target + (world.Map.DistanceToEdge(target, delta) + cordon).Length * delta / 1024;
			world.AddFrameEndTask(w =>
			{
				var flyer = w.CreateActor(species, new TypeDictionary
				{
					new CenterPositionInit(start), new OwnerInit(w.Players.First(p => p.InternalName == "Creeps")), new FacingInit(facing)
				});
				flyer.QueueActivity(false, new Fly(flyer, Target.FromPos(finish)));
				flyer.QueueActivity(new RemoveSelf());
			});
		}
	}
}
