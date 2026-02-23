using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    public static class SchematicLoader
    {
        public static Point ReadSize(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            reader.ReadString(); // name
            int versionRaw = reader.ReadInt32();
            if (versionRaw <= 10000)
                throw new InvalidDataException($"Unsupported schematic version {versionRaw}");
            ReadBitArray(reader); // tileFrameImportant
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            return new Point(width, height);
        }

        public static Point Paste(string filePath, int worldX, int worldY)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            string name = reader.ReadString();
            int versionRaw = reader.ReadInt32();

            if (versionRaw <= 10000)
                throw new InvalidDataException($"Unsupported schematic version {versionRaw} in {filePath}");

            int version = versionRaw - 10000;

            bool[] tileFrameImportant = ReadBitArray(reader);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();

            for (int x = 0; x < width; x++)
            {
                int y = 0;
                while (y < height)
                {
                    DeserializeAndPlace(reader, tileFrameImportant, version, worldX + x, worldY + y, out int rle);
                    y++;
                    while (rle > 0 && y < height)
                    {
                        CopyTile(worldX + x, worldY + y - 1, worldX + x, worldY + y);
                        y++;
                        rle--;
                    }
                }
            }

            SendTileRect(worldX, worldY, width, height);

            TShock.Log.ConsoleInfo($"[CCTG] Schematic '{name}' ({width}x{height}) pasted at ({worldX},{worldY})");
            return new Point(width, height);
        }

        private static void DeserializeAndPlace(BinaryReader r, bool[] tileFrameImportant, int version, int wx, int wy, out int rle)
        {
            byte header4 = 0;
            byte header3 = 0;
            byte header2 = 0;
            byte header1 = r.ReadByte();

            bool hasHeader2 = (header1 & 0x01) != 0;
            if (hasHeader2) header2 = r.ReadByte();

            bool hasHeader3 = hasHeader2 && (header2 & 0x01) != 0;
            if (hasHeader3) header3 = r.ReadByte();

            bool hasHeader4 = version >= 269 && hasHeader3 && (header3 & 0x01) != 0;
            if (hasHeader4) header4 = r.ReadByte();

            bool isActive = (header1 & 0x02) != 0;

            if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY)
            {
                SkipTileData(r, header1, header2, header3, header4, tileFrameImportant, version, isActive, out rle);
                return;
            }

            var tile = Main.tile[wx, wy];
            tile.ClearEverything();

            if (isActive)
            {
                int tileType;
                if ((header1 & 0x20) == 0)
                {
                    tileType = r.ReadByte();
                }
                else
                {
                    byte lo = r.ReadByte();
                    tileType = (r.ReadByte() << 8) | lo;
                }

                tile.type = (ushort)tileType;
                tile.active(true);

                if (tileType < tileFrameImportant.Length && tileFrameImportant[tileType])
                {
                    tile.frameX = r.ReadInt16();
                    tile.frameY = r.ReadInt16();
                }
                else
                {
                    tile.frameX = -1;
                    tile.frameY = -1;
                }

                if ((header3 & 0x08) != 0)
                    tile.color(r.ReadByte());
            }

            if ((header1 & 0x04) != 0)
            {
                tile.wall = r.ReadByte();
                if ((header3 & 0x10) != 0)
                    tile.wallColor(r.ReadByte());
            }

            byte liquidType = (byte)((header1 & 0x18) >> 3);
            if (liquidType != 0)
            {
                tile.liquid = r.ReadByte();
                tile.liquidType(liquidType == 2 ? 1 : liquidType == 3 ? 2 : 0);
            }

            if (header2 > 1)
            {
                if ((header2 & 0x02) != 0) tile.wire(true);
                if ((header2 & 0x04) != 0) tile.wire2(true);
                if ((header2 & 0x08) != 0) tile.wire3(true);

                byte brickStyle = (byte)((header2 & 0x70) >> 4);
                if (brickStyle == 1)
                    tile.halfBrick(true);
                else if (brickStyle >= 2)
                    tile.slope((byte)(brickStyle - 1));
            }

            if (header3 > 1)
            {
                if ((header3 & 0x02) != 0) tile.actuator(true);
                if ((header3 & 0x04) != 0) tile.inActive(true);
                if ((header3 & 0x20) != 0) tile.wire4(true);

                if (version >= 222 && (header3 & 0x40) != 0)
                    tile.wall = (ushort)(r.ReadByte() << 8 | tile.wall);
            }

            if (version >= 269 && hasHeader4 && header4 > 1)
            {
                // invisible/fullbright flags — no Terraria.Tile API, skip value only
            }

            byte rleType = (byte)((header1 & 0xC0) >> 6);
            rle = rleType switch
            {
                0 => 0,
                1 => r.ReadByte(),
                _ => r.ReadInt16()
            };
        }

        private static void SkipTileData(BinaryReader r, byte h1, byte h2, byte h3, byte h4,
            bool[] tfi, int version, bool isActive, out int rle)
        {
            if (isActive)
            {
                int tileType;
                if ((h1 & 0x20) == 0)
                    tileType = r.ReadByte();
                else
                {
                    byte lo = r.ReadByte();
                    tileType = (r.ReadByte() << 8) | lo;
                }

                if (tileType < tfi.Length && tfi[tileType])
                {
                    r.ReadInt16(); r.ReadInt16();
                }
                if ((h3 & 0x08) != 0) r.ReadByte();
            }

            if ((h1 & 0x04) != 0)
            {
                r.ReadByte();
                if ((h3 & 0x10) != 0) r.ReadByte();
            }

            byte liq = (byte)((h1 & 0x18) >> 3);
            if (liq != 0) r.ReadByte();

            if (version >= 222 && (h3 & 0x40) != 0) r.ReadByte();

            byte rleType = (byte)((h1 & 0xC0) >> 6);
            rle = rleType switch
            {
                0 => 0,
                1 => r.ReadByte(),
                _ => r.ReadInt16()
            };
        }

        private static void CopyTile(int srcX, int srcY, int dstX, int dstY)
        {
            if (dstX < 0 || dstX >= Main.maxTilesX || dstY < 0 || dstY >= Main.maxTilesY) return;
            if (srcX < 0 || srcX >= Main.maxTilesX || srcY < 0 || srcY >= Main.maxTilesY) return;
            Main.tile[dstX, dstY].CopyFrom(Main.tile[srcX, srcY]);
        }

        private static bool[] ReadBitArray(BinaryReader reader)
        {
            int length = reader.ReadInt16();
            var result = new bool[length];
            byte data = 0;
            byte mask = 128;
            for (int i = 0; i < length; i++)
            {
                if (mask != 128)
                {
                    mask = (byte)(mask << 1);
                }
                else
                {
                    data = reader.ReadByte();
                    mask = 1;
                }
                result[i] = (data & mask) == mask;
            }
            return result;
        }

        private static void SendTileRect(int x, int y, int width, int height)
        {
            const int chunkSize = 150;
            for (int cx = x; cx < x + width; cx += chunkSize)
            {
                for (int cy = y; cy < y + height; cy += chunkSize)
                {
                    int w = Math.Min(chunkSize, x + width - cx);
                    int h = Math.Min(chunkSize, y + height - cy);
                    TSPlayer.All.SendTileRect((short)cx, (short)cy, (byte)Math.Min(w, 255), (byte)Math.Min(h, 255));
                }
            }
        }
    }
}
