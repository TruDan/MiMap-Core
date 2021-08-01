﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using log4net;
using MiMap.Common.Data;
using MiMap.Common.Net;
using MiMap.Common.Net.Data;
using MiMap.Drawing;
using MiMap.Drawing.Events;
using MiMap.Web.Sockets;
using MiNET;
using MiNET.Blocks;
using MiNET.Utils;
using MiNET.Utils.Vectors;
using MiNET.Worlds;

namespace MiMap.Plugin.Runners
{
    public class LevelRunner
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(LevelRunner));

        public const int UpdateInterval = 500;
        public const int MaxChunksPerInterval = 2048;

        private readonly MiNetServer _server;

        public LevelMap Map { get; }

        private Level Level { get; set; }

        private bool _init = false;

        private readonly List<ChunkCoordinates> _renderedChunks = new List<ChunkCoordinates>();

        private readonly Timer _timer;
        private readonly object _updateSync = new object();


        public LevelRunner(MiNetServer server, LevelMap map)
        {
            _server = server;
            Map = map;

            Map.OnTileUpdated += MapOnOnTileUpdated;

            _timer = new Timer(DoUpdate);
        }

        private void MapOnOnTileUpdated(object sender, TileUpdateEventArgs e)
        {
            WsServer.BroadcastTileUpdate(new TileUpdatePacket()
            {
                LayerId = e.LevelId + "_" + e.LayerId,
                Tile = new Tile()
                {
                    X = e.TileX,
                    Y = e.TileY,
                    Zoom = e.TileZoom
                }
            });
        }

        public void Start()
        {
            _timer.Change(UpdateInterval, UpdateInterval);
        }

        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void DoUpdate(object state)
        {
            if (!Monitor.TryEnter(_updateSync))
            {
                return;
            }

            try
            {
                UpdateLevel();
            }
            finally
            {
                Monitor.Exit(_updateSync);
            }
        }

        private void UpdateLevel()
        {
            TryGetLevel();
            if (Level == null) return;

            if (!_init)
            {
                _init = true;

                var chunkGen = Config.GetProperty("GenChunks", 64);
                if (chunkGen > 0)
                {
                    ThreadPool.QueueUserWorkItem(o =>
                    {
                        //for (int j = 0; j < chunkGen; j++)
                        //{
                        GenerateChunks(Level, chunkGen);
                        //}
                    });
                }
            }
            return;
        }

        private void GenerateChunks(Level level, int r)
        {
            var cX = (int)level.SpawnPoint.X >> 4;
            var cZ = (int)level.SpawnPoint.Z >> 4;

            var x = 0;
            var y = 0;
            var t = r;
            var dx = 0;
            var dy = -1;

            for (var i = 0; i < (r * r); i++)
            {
                if ((-r / 2 <= x) && (x <= r / 2) && (-r / 2 <= y) && (y <= r / 2))
                {
                    //Log.InfoFormat("Generating Chunk {0},{1}", x, y);
                    var coords = new ChunkCoordinates(cX + x, cZ + y);
                    //ThreadPool.QueueUserWorkItem(c => level.GetChunk((ChunkCoordinates) c), coords);

                    var chunk = level.GetChunk(coords);
                    RenderChunk(chunk);
                    (level.WorldProvider as ICachingWorldProvider)?.ClearCachedChunks();
                    //Thread.Sleep(50);
                }

                if ((x == y) || ((x < 0) && (x == -y)) || ((x > 0) && (x == 1 - y)))
                {
                    t = dx;
                    dx = -dy;
                    dy = t;
                }

                x += dx;
                y += dy;
            }


            /*
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {

                    var coords = new ChunkCoordinates(dx, dz);
                    //ThreadPool.QueueUserWorkItem(c => level.GetChunk((ChunkCoordinates) c), coords);

                    var chunk = level.GetChunk(coords);
                    RenderChunk(chunk);
                    (level.WorldProvider as ICachingWorldProvider)?.ClearCachedChunks();

                }
            }*/
        }

        private void RenderChunk(ChunkColumn chunk)
        {
            _renderedChunks.Add(new ChunkCoordinates(chunk.X, chunk.Z));
            chunk.RecalcHeight();

            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var meta = GetColumnMeta(chunk, x, z);
                    Map.UpdateBlockColumn(meta);
                }
            }
        }

        private BlockColumnMeta GetColumnMeta(ChunkColumn chunk, int x, int z)
        {
            var pos = new BlockPosition((chunk.X << 4) + x, (chunk.Z << 4) + z);

            var y = chunk.GetHeight(x, z);
            //var highestBlock = Level.GetBlock(pos.X, y, pos.Z);
            var highestBlock = GetHighestBlock(pos.X, pos.Z);

            if (highestBlock == null)
            {
                return new BlockColumnMeta()
                {
                    Position = pos,
                    Height = 0,
                    BiomeId = 0,
                    BlockId = 0,
                    LightLevel = 0
                };
            }

            //_skyLightCalculations.Calculate(Level, highestBlock);

            //if (highestBlock.LightLevel > 0)
            //{
            //    BlockLightCalculations.Calculate(Level, highestBlock);
            //}

            return new BlockColumnMeta()
            {
                Position = pos,
                Height = (byte)highestBlock.Coordinates.Y,
                BiomeId = highestBlock.BiomeId,
                BlockId = highestBlock.Id,
                LightLevel = chunk.GetSkylight(x, y, z)
            };
        }

        private Block GetHighestBlock(int x, int z)
        {
            for (int y = 255; y > 0; y--)
            {
                var block = Level.GetBlock(x, y, z);
                //if (!block.IsTransparent)
                if (block.IsSolid || block is Flowing)
                {
                    return block;
                }
            }
            return null;
        }

        private void TryGetLevel()
        {
            if (Level != null) return;

            var lm = _server.LevelManager;
            if (lm == null) return;

            Level = lm.Levels.FirstOrDefault(l => l != null &&
                                                  l.LevelId.Equals(Map.Config.LevelId,
                                                      StringComparison.InvariantCultureIgnoreCase));

            if (Level != null)
            {
                WsServer.BroadcastPacket(new LevelMetaPacket()
                {
                    LevelId = Level.LevelId,
                    Meta = Map.Meta
                });
            }
        }
    }
}
