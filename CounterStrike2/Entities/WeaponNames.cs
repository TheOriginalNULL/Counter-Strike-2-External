using System.Collections.Generic;

namespace CounterStrike2.Entities
{
    internal static class WeaponNames
    {
        // CS2 item definition indices (CEconItemDefinition::m_iItemDefinitionIndex).
        // Knives are 42/59 (default) or 500+ (skin variants) — all displayed as "Knife".
        private static readonly Dictionary<ushort, string> _map = new()
        {
            // ── Pistols ──
            { 1,  "Deagle"     }, { 2,  "Dualies"   }, { 3,  "Five-SeveN" },
            { 4,  "Glock"      }, { 30, "Tec-9"      }, { 32, "P2000"      },
            { 36, "P250"       }, { 61, "USP-S"      }, { 63, "CZ75"       },
            { 64, "R8"         },

            // ── SMGs ──
            { 17, "MAC-10"  }, { 19, "P90"     }, { 23, "MP5-SD" },
            { 24, "UMP-45"  }, { 26, "PP-Bizon"}, { 33, "MP7"    },
            { 34, "MP9"     },

            // ── Rifles ──
            { 7,  "AK-47"   }, { 8,  "AUG"     }, { 9,  "AWP"    },
            { 10, "FAMAS"   }, { 11, "G3SG1"   }, { 13, "Galil"  },
            { 16, "M4A4"    }, { 38, "SCAR-20" }, { 39, "SG 553" },
            { 40, "SSG 08"  }, { 60, "M4A1-S"  },

            // ── Heavy ──
            { 14, "M249"      }, { 25, "XM1014"    },
            { 27, "MAG-7"     }, { 28, "Negev"     }, { 29, "Sawed-Off" },
            { 35, "Nova"      },

            // ── Grenades / utility ──
            { 31, "Zeus"   }, { 43, "Flash"      }, { 44, "HE"     },
            { 45, "Smoke"  }, { 46, "Molotov"    }, { 47, "Decoy"  },
            { 48, "Incend" }, { 49, "C4"         },

            // ── Knives (defaults) ──
            { 42, "Knife"  }, { 59, "Knife" },
        };

        public static string Get(ushort id)
        {
            if (id == 0) return string.Empty;
            if (id >= 500) return "Knife";             // skin-variant knives
            return _map.TryGetValue(id, out var n) ? n : $"#{id}";
        }
    }
}
