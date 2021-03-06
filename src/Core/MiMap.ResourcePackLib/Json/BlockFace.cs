using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MiMap.ResourcePackLib.Json
{
	[JsonConverter(typeof(StringEnumConverter))]
	public enum BlockFace
	{
		Down = 0,
		Up = 1,
		East = 2,
		West = 3,
		North = 4,
		South = 5,
		None = 255,
	}
}
