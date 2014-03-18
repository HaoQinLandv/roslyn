﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using CustomAttribute;

[assembly: @AttrName()]
[assembly: @AttrName(UShortField = 321)]
[module: AttrNameAttribute(TypeField = typeof(Dictionary<string, int>))]

namespace AttributeUse
{
    // attribute on type parameter (with target typevar or not)
    public interface IFoo<[typevar: AllInheritMultiple(3.1415926)] T, [AllInheritMultiple('q', 2)] V>
    {
        // default: method
        [AllInheritMultiple(p3:1.234f, p2: 1056, p1: "555")]
        // attribute on return, param
        [return: AllInheritMultiple("obj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)]
        V Method([param: DerivedAttribute(new sbyte[] {-1, 0, 1}, ObjectField = typeof(IList<>))]T t);
    }

    // multiple attributes
    [AllInheritMultiple(new char[] { '1', '2' }, UIntField = 112233)]
    [type: AllInheritMultiple(new char[] { 'a', '\0', '\t' }, AryField = new ulong[] { 0, 1, ulong.MaxValue })]
    [AllInheritMultiple(null, "", null, "1234", AryProp = new object[2] { new ushort[] { 1 }, new ushort[] { 2, 3, 4 } })]
    public class Foo<[typevar: AllInheritMultiple(null), AllInheritMultiple()] T> : IFoo<T, ushort>
    {
        // named parameters
        [field: AllInheritMultiple(p2: System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public, p1: -123)]
        [AllInheritMultiple(p1: 111, p2: System.Reflection.BindingFlags.NonPublic)]
        public int ClassField;

        [property: BaseAttribute(-1)]
        public Foo<char> Prop
        {
            // return: NYI
            [AllInheritMultiple(1, 2, 3), AllInheritMultiple(4, 5, 1.1f)]
            get;
            [param: DerivedAttribute(-3)]
            set;
        }

        [AllInheritMultiple(+007, 256)]
        [AllInheritMultiple(-008, 255)]
        [method: DerivedAttribute(typeof(IFoo<short, ushort>), ObjectField = 1)]
        public ushort Method(T t) { return 0; }
        // Explicit NotImpl
        // ushort IFoo<T, ushort>.Method(T t) { return 0; }
    }
}