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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace Hackathon.Framework.GameDevWare.Serialization.Metadata
{
	internal class TypeDescription : MemberDescription
	{
		private static readonly Dictionary<Type, TypeDescription> TypeDescriptions = new Dictionary<Type, TypeDescription>();

		private readonly Type objectType;
		private readonly Func<object> constructorFn;
		private readonly ReadOnlyCollection<DataMemberDescription> members;
		private readonly Dictionary<string, DataMemberDescription> membersByName;

		public Type ObjectType { get { return this.objectType; } }
		public bool IsAnonymousType { get; private set; }
		public bool IsEnumerable { get; private set; }
		public bool IsDictionary { get; private set; }
		public bool IsDataContract { get; private set; }
		public bool IsSerializable { get; private set; }
		public ReadOnlyCollection<DataMemberDescription> Members { get { return this.members; } }

		public TypeDescription(Type objectType)
			: base(null, objectType)
		{
			if (objectType == null) throw new ArgumentNullException("objectType");

			this.objectType = objectType;
			this.IsDataContract = this.Attributes.Any(attribute => attribute.GetType().Name == DATA_CONTRACT_ATTRIBUTE_NAME);
			this.IsSerializable = objectType.IsSerializable;
			this.IsEnumerable = objectType.IsInstantiationOf(typeof(Enumerable)) && objectType != typeof(string);
			this.IsDictionary = typeof(IDictionary).IsAssignableFrom(objectType);
			this.IsAnonymousType = objectType.IsSealed && objectType.IsNotPublic && objectType.GetCustomAttributes(typeof(CompilerGeneratedAttribute), true).Length > 0; ;

			var allMembers = this.FindMembers(objectType);

			this.members = allMembers.AsReadOnly();
			this.membersByName = allMembers.ToDictionary(m => m.Name, StringComparer.Ordinal);

			GettersAndSetters.TryGetConstructor(objectType, out this.constructorFn);
		}

		private List<DataMemberDescription> FindMembers(Type objectType)
		{
			if (objectType == null) throw new ArgumentNullException("objectType");

			var members = new List<DataMemberDescription>();
			var memberNames = new HashSet<string>(StringComparer.Ordinal);

			var isOptIn = objectType.GetCustomAttributes(false).Any(a => a.GetType().Name == DATA_CONTRACT_ATTRIBUTE_NAME);
			var searchFlags = BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | (isOptIn ? BindingFlags.NonPublic : 0);
			var properties = objectType.GetProperties(searchFlags);
			var fields = objectType.GetFields(searchFlags);

			foreach (var member in properties.Cast<MemberInfo>().Concat(fields.Cast<MemberInfo>()))
			{
				if (member is PropertyInfo && (member as PropertyInfo).GetIndexParameters().Length != 0)
					continue;

				var dataMemberAttribute = member.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name == DATA_MEMBER_ATTRIBUTE_NAME);
				var ignoreMemberAttribute = member.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name == IGNORE_DATA_MEMBER_ATTRIBUTE_NAME);

				if (isOptIn && dataMemberAttribute == null)
					continue;
				else if (!isOptIn && ignoreMemberAttribute != null)
					continue;

				var dataMember = default(DataMemberDescription);
				if (member is PropertyInfo) dataMember = new PropertyDescription(this, member as PropertyInfo);
				else if (member is FieldInfo) dataMember = new FieldDescription(this, member as FieldInfo);
				else throw new InvalidOperationException("Unknown member type. Should be PropertyInfo or FieldInfo.");

				if (string.IsNullOrEmpty(dataMember.Name))
					throw JsonSerializationException.TypeIsNotValid(objectType, "has no members with empty name");

				if (memberNames.Contains(dataMember.Name))
				{
					var conflictingMember = members.First(m => m.Name == dataMember.Name);
					throw JsonSerializationException.TypeIsNotValid(objectType, string.Format("has no duplicate member's name '{0}' ('{1}.{2}' and '{3}.{4}')", dataMember.Name, conflictingMember.Member.DeclaringType.Name, conflictingMember.Member.Name, dataMember.Member.DeclaringType.Name, dataMember.Member.Name));
				}

				members.Add(dataMember);
				memberNames.Add(dataMember.Name);
			}

			return members;
		}

		public bool TryGetMember(string name, out DataMemberDescription member)
		{
			return this.membersByName.TryGetValue(name, out member);
		}

		public object CreateInstance()
		{
			if (this.constructorFn != null)
				return this.constructorFn();
			else
				return Activator.CreateInstance(this.objectType);
		}

		public static TypeDescription Get(Type type)
		{
			if (type == null) throw new ArgumentNullException("type");

			lock (TypeDescriptions)
			{
				TypeDescription objectTypeDescription;
				if (!TypeDescriptions.TryGetValue(type, out objectTypeDescription))
					TypeDescriptions.Add(type, objectTypeDescription = new TypeDescription(type));
				return objectTypeDescription;
			}
		}

		public override string ToString()
		{
			return this.objectType.ToString();
		}
	}
}
