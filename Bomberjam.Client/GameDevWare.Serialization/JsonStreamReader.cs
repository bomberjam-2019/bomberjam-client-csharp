﻿/* 
	Copyright (c) 2016 Denis Zykov, GameDevWare.com

	This a part of "Json & MessagePack Serialization" Unity Asset - https://www.assetstore.unity3d.com/#!/content/59918

	THIS SOFTWARE IS DISTRIBUTED "AS-IS" WITHOUT ANY WARRANTIES, CONDITIONS AND 
	REPRESENTATIONS WHETHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION THE 
	IMPLIED WARRANTIES AND CONDITIONS OF MERCHANTABILITY, MERCHANTABLE QUALITY, 
	FITNESS FOR A PARTICULAR PURPOSE, DURABILITY, NON-INFRINGEMENT, PERFORMANCE 
	AND THOSE ARISING BY STATUTE OR FROM CUSTOM OR USAGE OF TRADE OR COURSE OF DEALING.
	
	This source code is distributed via Unity Asset Store, 
	to use it in your project you should accept Terms of Service and EULA 
	https://unity3d.com/ru/legal/as_terms
*/

using System;
using System.IO;

// ReSharper disable once CheckNamespace
namespace Hackathon.Framework.GameDevWare.Serialization
{
	public sealed class JsonStreamReader : JsonReader
	{
		private readonly StreamReader reader;

		public JsonStreamReader(Stream stream, SerializationContext context, int bufferSize = DEFAULT_BUFFER_SIZE)
			: base(context, bufferSize)
		{
			if (stream == null) throw new ArgumentNullException("stream");
			if (!stream.CanRead) throw JsonSerializationException.StreamIsNotReadable();


			reader = new StreamReader(stream, context.Encoding);
		}

		protected override int FillBuffer(char[] buffer, int index)
		{
			if (buffer == null)
				throw new ArgumentNullException("buffer");
			if (index < 0 || index >= buffer.Length)
				throw new ArgumentOutOfRangeException("index");


			var count = buffer.Length - index;
			if (count <= 0)
				return index;

			var readed = reader.Read(buffer, index, count);
			return index + readed;
		}
	}
}
