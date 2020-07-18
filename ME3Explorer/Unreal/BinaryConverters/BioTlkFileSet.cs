﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ME3Explorer.Packages;

namespace ME3Explorer.Unreal.BinaryConverters
{
    public class BioTlkFileSet : ObjectBinary
    {
        public OrderedMultiValueDictionary<NameReference, BioTlkSet> TlkSets;

        protected override void Serialize(SerializingContainer2 sc)
        {
            if (sc.Game != MEGame.ME1)
            {
                throw new Exception($"BioTlkFileSet is not a valid class for {sc.Game}!");
            }
            sc.Serialize(ref TlkSets, SCExt.Serialize, (SerializingContainer2 sc2, ref BioTlkSet tlkSet) =>
            {
                if (sc2.IsLoading)
                {
                    tlkSet = new BioTlkSet();
                }
                sc2.SerializeConstInt(2);
                sc2.Serialize(ref tlkSet.Male);
                sc2.Serialize(ref tlkSet.Female);
            });
        }

        public override List<(UIndex, string)> GetUIndexes(MEGame game)
        {
            var uIndexes = new List<(UIndex, string)>();
            foreach ((NameReference lang, BioTlkSet bioTlkSet) in TlkSets)
            {
                uIndexes.Add(bioTlkSet.Male, $"{lang}: Male");
                uIndexes.Add(bioTlkSet.Female, $"{lang}: Female");
            }
            return uIndexes;
        }

        public class BioTlkSet
        {
            public UIndex Male;
            public UIndex Female;
        }
    }
}
