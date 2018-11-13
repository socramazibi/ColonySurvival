﻿using BlockTypes;
using Jobs;
using NPC;

namespace Pipliz.Mods.BaseGame.AreaJobs
{
	[AreaJobDefinitionAutoLoader]
	public class TemperateForesterDefinition : AbstractAreaJobDefinition<TemperateForesterDefinition>
	{
		public TemperateForesterDefinition ()
		{
			Identifier = "pipliz.temperateforest";
			fileName = "temperateforester";
			UsedNPCType = NPCType.GetByKeyNameOrDefault("pipliz.forester");
			AreaType = Shared.EAreaType.Forestry;
		}

		public override IAreaJob CreateAreaJob (Colony owner, Vector3Int min, Vector3Int max, bool isLoaded, int npcID = 0)
		{
			if (!isLoaded) {
				TurnArableIntoDirt(min, max, owner);
			}
			return new ForesterJob(owner, min, max, npcID);
		}

		static bool ChopTree (Vector3Int p, BlockChangeRequestOrigin origin)
		{
			ItemTypes.ItemType logType = ItemTypes.GetType("logtemperate");
			ItemTypes.ItemType air = ItemTypes.Air;
			for (int y = 0; y < 5; y++) {
				switch (ServerManager.TryChangeBlock(p.Add(0, y, 0), logType, air, origin)) {
					case EServerChangeBlockResult.CancelledByCallback:
					case EServerChangeBlockResult.ChunkNotReady:
					default:
						return false;
					case EServerChangeBlockResult.Success:
						break;
					case EServerChangeBlockResult.UnexpectedOldType:
						y = 15;
						break;
				}
			}
			ItemTypes.ItemType leavesType = ItemTypes.GetType("leavestemperate");
			System.Collections.Generic.List<Vector3Int> leavesOffsets = GrowableBlocks.TemperateSapling.Leaves;
			for (int i = 0; i < leavesOffsets.Count; i++) {
				switch (ServerManager.TryChangeBlock(p + leavesOffsets[i], leavesType, air, origin)) {
					case EServerChangeBlockResult.CancelledByCallback:
					case EServerChangeBlockResult.ChunkNotReady:
					default:
						return false;
					case EServerChangeBlockResult.Success:
					case EServerChangeBlockResult.UnexpectedOldType:
						break;
				}
			}
			return true;
		}

		public class ForesterJob : AbstractAreaJob<TemperateForesterDefinition>
		{
			// store treeLocation separately from positionSub because the farmer will move next to these positions(they're not equal)
			protected Vector3Int treeLocation = Vector3Int.invalidPos;
			static ItemTypes.ItemType[] yTypesBuffer = new ItemTypes.ItemType[5]; // max 3 Y + 1 below + 1 above

			public ForesterJob (Colony owner, Vector3Int min, Vector3Int max, int npcID = 0) : base(owner, min, max, npcID) { }

			public override void CalculateSubPosition ()
			{
				ThreadManager.AssertIsMainThread();
				bool hasSeeds = NPC.Colony.Stockpile.Contains(BuiltinBlocks.Sapling);
				Vector3Int min = Minimum;
				Vector3Int max = Maximum;
				int ySize = max.y - min.y + 1;

				for (int x = min.x + 1; x < max.x; x += 3) {
					for (int z = min.z + 1; z < max.z; z += 3) {
						for (int y = -1; y <= ySize; y++) {
							if (!World.TryGetTypeAt(new Vector3Int(x, min.y + y, z), out yTypesBuffer[y + 1])) {
								goto DUMB_RANDOM;
							}
						}

						for (int y = 0; y < ySize; y++) {
							ItemTypes.ItemType typeBelow = yTypesBuffer[y];
							ItemTypes.ItemType type = yTypesBuffer[y + 1];
							ItemTypes.ItemType typeAbove = yTypesBuffer[y + 2];

							if ((type == ItemTypes.Air && hasSeeds) || type.ItemIndex == BuiltinBlocks.LogTemperate) {
								if ((typeAbove.ItemIndex != BuiltinBlocks.Air && typeAbove.ItemIndex != BuiltinBlocks.LogTemperate) || !typeBelow.IsFertile) {
									continue; // check next Y layer
								}
								treeLocation = new Vector3Int(x, min.y + y, z);
								positionSub = AI.AIManager.ClosestPositionNotAt(treeLocation, NPC.Position);
								return;
							}
						}
					}
				}

				for (int i = 0; i < 5; i++) {
					// give the random positioning 5 chances to become valid
					Vector3Int test = min.Add(
						Random.Next(0, (max.x - min.x) / 3) * 3,
						0,
						Random.Next(0, (max.z - min.z) / 3) * 3
					);

					for (int y = -1; y <= ySize; y++) {
						if (!World.TryGetTypeAt(test.Add(0, y, 0), out yTypesBuffer[y + 1])) {
							goto DUMB_RANDOM;
						}
					}

					for (int y = 0; y < ySize; y++) {
						ItemTypes.ItemType  typeBelow = yTypesBuffer[y];
						ItemTypes.ItemType  type = yTypesBuffer[y + 1];
						ItemTypes.ItemType typeAbove = yTypesBuffer[y + 2];

						if (!typeBelow.BlocksPathing || type.BlocksPathing || typeAbove.BlocksPathing) {
							continue; // check next Y layer
						}

						positionSub = test.Add(0, y, 0);
						treeLocation = Vector3Int.invalidPos;
						return;
					}
				}

				DUMB_RANDOM:
				positionSub = min.Add(
					Random.Next(0, (max.x - min.x) / 3) * 3,
					(max.x - min.x) / 2,
					Random.Next(0, (max.z - min.z) / 3) * 3
				);
			}

			static System.Collections.Generic.List<ItemTypes.ItemTypeDrops> GatherResults = new System.Collections.Generic.List<ItemTypes.ItemTypeDrops>();

			public override void OnNPCAtJob (ref NPCBase.NPCState state)
			{
				ThreadManager.AssertIsMainThread();
				state.JobIsDone = true;
				positionSub = Vector3Int.invalidPos;
				Vector3Int min = Minimum;
				Vector3Int max = Maximum;
				if (!treeLocation.IsValid) { // probably idling about
					state.SetCooldown(Random.NextFloat(8f, 16f));
					return;
				}

				ushort type;
				if (!World.TryGetTypeAt(treeLocation, out type)) {
					state.SetCooldown(10.0);
					return;
				}

				if (type == BuiltinBlocks.LogTemperate) {
					if (ChopTree(treeLocation, Owner)) {
						state.SetIndicator(new Shared.IndicatorState(10f, BuiltinBlocks.LogTemperate));
						ServerManager.SendAudio(treeLocation.Vector, "woodDeleteHeavy");

						GatherResults.Clear();
						GatherResults.Add(new ItemTypes.ItemTypeDrops(BuiltinBlocks.LogTemperate, 3, 1.0));
						GatherResults.Add(new ItemTypes.ItemTypeDrops(BuiltinBlocks.LeavesTemperate, 9, 1.0));
						GatherResults.Add(new ItemTypes.ItemTypeDrops(BuiltinBlocks.Sapling, 1, 1.0));
						GatherResults.Add(new ItemTypes.ItemTypeDrops(BuiltinBlocks.Sapling, 1, 0.25));

						ModLoader.TriggerCallbacks(ModLoader.EModCallbackType.OnNPCGathered, this as IJob, treeLocation, GatherResults);

						NPC.Inventory.Add(GatherResults);
					} else {
						state.SetCooldown(Random.NextFloat(3f, 6f));
					}
					return;
				}

				if (type == 0) {
					// maybe plant sapling?
					if (World.TryGetTypeAt(treeLocation.Add(0, -1, 0), out ItemTypes.ItemType typeBelow)) {
						if (typeBelow.IsFertile) {
							if (NPC.Inventory.TryGetOneItem(BuiltinBlocks.Sapling) || NPC.Colony.Stockpile.TryRemove(BuiltinBlocks.Sapling)) {
								ServerManager.TryChangeBlock(treeLocation, 0, BuiltinBlocks.Sapling, Owner, ESetBlockFlags.DefaultAudio);
								state.SetCooldown(2.0);
								return;
							} else {
								state.SetIndicator(new Shared.IndicatorState(6f, BuiltinBlocks.Sapling, true, false));
								return;
							}
						}
					} else {
						state.SetCooldown(10.0);
						return;
					}

				}

				// something unexpected
				state.SetCooldown(Random.NextFloat(8f, 16f));
				return;
			}
		}
	}
}
