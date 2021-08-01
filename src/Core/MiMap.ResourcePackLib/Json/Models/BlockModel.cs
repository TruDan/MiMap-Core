﻿using System.Collections.Generic;
using System.Drawing;
using Newtonsoft.Json;

namespace MiMap.ResourcePackLib.Json.Models
{
	public class BlockModel
	{
		[JsonIgnore]
		public string Name { get; set; }

		/// <summary>
		/// Loads a different model from the given path, starting in assets/minecraft/models. If both "parent" and "elements" are set, the "elements" tag overrides the "elements" tag from the previous model.
		/// 
		/// Can be set to "builtin/generated" to use a model that is created out of the specified icon. Note that only the first layer is supported, and rotation can only be achieved using block states files.
		/// </summary>
		[JsonProperty("parent")]
		public string ParentName { get; set; }

		[JsonIgnore]
		public BlockModel Parent { get; set; }

		/// <summary>
		/// Whether to use ambient occlusion (true - default), or not (false).
		/// </summary>
		public bool AmbientOcclusion { get; set; } = true;

		/// <summary>
		/// Holds the textures of the model. Each texture starts in assets/minecraft/textures or can be another texture variable.
		/// </summary>
		[JsonProperty("textures")]
		public Dictionary<string, string> TextureDefinitions { get; set; } = new Dictionary<string, string>();

		[JsonIgnore]
		public Dictionary<string, Bitmap> Textures { get; set; } = new Dictionary<string, Bitmap>();

		/// <summary>
		/// Contains all the elements of the model. they can only have cubic forms. If both "parent" and "elements" are set, the "elements" tag overrides the "elements" tag from the previous model.
		/// </summary>
		//[JsonConverter(typeof(MCElementsDictionaryConverter))]
		//public Dictionary<string, BlockModelElement> Elements { get; set; } = new Dictionary<string, BlockModelElement>();
		public BlockModelElement[] Elements { get; set; } = new BlockModelElement[0];
	}
}
