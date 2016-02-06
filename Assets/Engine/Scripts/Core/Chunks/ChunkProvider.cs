using System;
using System.IO;
using System.Text;
using Assets.Engine.Scripts.Common.IO;
using Assets.Engine.Scripts.Common.IO.RLE;
using Assets.Engine.Scripts.Core.Chunks;
using Assets.Engine.Scripts.Generators;
using Assets.Engine.Scripts.Core;
using Assets.Engine.Scripts.Core.Blocks;
using UnityEngine;
using Assets.Engine.Scripts.Generators.Terrain;

namespace Assets.Engine.Scripts.Provider
{
    /// <summary>
    ///     A purely local chunk provider
    /// </summary>
    public class ChunkProvider: IChunkProvider
    {
        #region Public Properties

        // Persistent data to be stored in <disk>:/Users/<user>/AppData/LocalLow/RBiely/Voxe/VoxelData
        private static readonly string DataPath = string.Format("{0}/{1}", Application.persistentDataPath, "VoxelData");

        /// <summary>
        ///     Gets the local clientside map.
        /// </summary>
        public Map LocalMap { get; private set; }

        #endregion Public Properties

        #region Private vars

        // chunk generator
        public readonly IChunkGenerator ChunkGenerator;

        // String builder used in the main thread to determine a file path for a given chunk
        public static readonly StringBuilder FilePathStringBuilder = new StringBuilder(DataPath.Length+21);

        #endregion Private vars

        #region Constructor

        public ChunkProvider(Map map)
        {
            Map.Current = LocalMap = map;
            map.ChunkProvider = this;

            //ChunkGenerator = new SimplePerlinGenerator();
            //ChunkGenerator = new SolidChunkGenerator();
            ChunkGenerator = new SimpleTerrainGenerator();
        }

        #endregion Constructor

        #region Public Methods

        /// <summary>
        ///     Update the server.
        /// </summary>
        public void Update()
        {
            LocalMap.UpdateMap();
        }

        /// <summary>
        ///     Shutdown this server.
        /// </summary>
        public void Shutdown()
        {
            LocalMap.Shutdown();
        }

        public static string GetFilePathFromIndex(int cx, int cz)
        {
            // E.g. D:\VoxelData\0FF21_22F00_00001.chn
            FilePathStringBuilder.Remove(0, FilePathStringBuilder.Length);
            FilePathStringBuilder.AppendFormat(@"{0}\{1}_{2}.chn", DataPath, cx.ToString("X8"), cz.ToString("X8"));
            return FilePathStringBuilder.ToString();
        }

        #endregion Public Methods

        #region Threading

        private static bool LoadChunkFromDisk(Chunk chunk, string filePath)
        {
            try
            {
                // Read data from file
                byte[] filedata;

                FileStream fs = null;
                try
                {
                    fs = new FileStream(filePath, FileMode.Open);
                    using (var br = new BinaryReader(fs))
                    {
                        // Read filled block count
                        for (int i = 0; i < EngineSettings.ChunkConfig.StackSize; i++)
                            chunk.Sections[i].NonEmptyBlocks = br.ReadInt16();

                        // Read section offsets
                        chunk.MaxRenderY = br.ReadInt16();
                        chunk.MinRenderY = br.ReadInt16();

                        // Read chunk data
                        int headerLen = (EngineSettings.ChunkConfig.StackSize << 1) + 4; // Size of previous data
                        filedata = br.ReadBytes((int)(fs.Length - headerLen));

                        fs = null;
                    }
                }
                finally
                {
                    if (fs!=null)
                        fs.Dispose();
                }

                // Convert byte array to array of BlockData structs
                chunk.RLE.Assign(StructSerializers.DeserializeArrayToList<RLEDataPair<BlockData>>(filedata));

                chunk.Blocks.Set(chunk.RLE.Decompress());
            }
            catch (Exception ex)
            {
                string s = string.Format("Cannot load chunk '{0}' ([{1},{2}]): {3}", filePath, chunk.Pos.X, chunk.Pos.Z, ex);
                Debug.LogError(s);
                return false;
            }
            
            return true;
        }

        

        public static bool StoreChunkToDisk(Chunk chunk, string filePath)
        {
            try
            {
                var buff = StructSerializers.SerializeArray(chunk.RLE.List);

                FileStream fs = null;
                try
                {
                    fs = new FileStream(filePath, FileMode.Create);
                    using (BinaryWriter bw = new BinaryWriter(fs))
                    {
                        fs = null;

                        // Store number of filled block for each section. Using short limits max section size to 16x16x16
                        for (int i = 0; i<EngineSettings.ChunkConfig.StackSize; i++)
                            bw.Write((short)chunk.Sections[i].NonEmptyBlocks);

                        // Store chunk offsets
                        bw.Write((short)chunk.MaxRenderY);
                        bw.Write((short)chunk.MinRenderY);

                        // Store block data
                        bw.Write(buff);
                    }
                }
                finally
                {
                    if (fs!=null)
                        fs.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.LogErrorFormat("Cannot save chunk '{0}' ([{1},{2}]): {3}", filePath, chunk.Pos.X, chunk.Pos.Z, ex);
                return false;
            }

            return true;
        }
        
        private static void RequestChunkFromDisk(Chunk chunk, string filePath)
        {
            Globals.IOPool.AddItem(arg =>
                {
                    if (LoadChunkFromDisk(chunk, filePath))
                    {
                        chunk.MarkAsLoaded();
                        chunk.RegisterNeighbors();
                        return;
                    }

                    // File could not be read for some reason, delete it
                    File.Delete(filePath);

                    // Request new data from the internet
                    GenerateChunk(chunk);
                },
                chunk);
        }

        #endregion Threading

        #region IChunkProvider implementation

        public IChunkGenerator GetGenerator()
        {
            return ChunkGenerator;
        }

        // load or generate a chunk
        public Chunk RequestChunk(int cx, int cz)
        {
            Chunk chunk = ObjectPoolProvider.Chunks.Pop();
            chunk.Init(cx, cz);
            
            if (EngineSettings.WorldConfig.Streaming)
            {
                string filePath = GetFilePathFromIndex(chunk.Pos.X, chunk.Pos.Z);
                if (File.Exists(filePath))
                {
                    // Data is on disk
                    RequestChunkFromDisk(chunk, filePath);
                }
                else
                {
                    // Data is not on disk, it cannot be open or decompressed or timestamp is too old
                    GenerateChunk(chunk);
                }

            }
            else
            {
                GenerateChunk(chunk);
            }

            return chunk;
        }

        // save chunk to disk
        public bool ReleaseChunk(Chunk chunk)
        {
            chunk.Reset(false);
            ObjectPoolProvider.Chunks.Push(chunk);

            return true;
        }

        #endregion IChunkProvider implementation

        private static void GenerateChunk(Chunk chunk)
        {
            chunk.RegisterNeighbors();
        }
    }
}