using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClipperLib;
using LeagueSharp;
using SharpDX;
using Color = System.Drawing.Color;
using Path = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;
using System.Text;
//Not tested / missing: Galio, Swain, Nocturne, Talon, Viktor, Vi, Jayce, Lulu, Lissandra, Lucian
namespace EvadeSharp
{
    internal class Program
    {
        public enum SkillShotType
        {
            SKILLSHOT_LINE,
            SKILLSHOT_CIRCLE,
            SKILLSHOT_CONE, /* actually it's a sector, doesnt support missilespeed yet*/
            SKILLSHOT_RING,
            SKILLSHOT_TRIANGLE,
            SKILLSHOT_NONE,
            /* Not added yet */
            SKILLSHOT_ARC,
        }
        /*Config*/
        private static bool ConfigEvadingEnabled = true;
        private static bool ConfigDrawingEnabled = true;
        private static bool ConfigDodgeOnlyDangerous = false;
        private static bool ConfigUseFlash = true;
        private static bool ConfigUseDashes = true;
        private static bool ConfigEvadeWalking = true;

        /**/
        private const int buffer = 8; /* Buffer added to all the skillshots width */
        private const int ExtraW = 10; /* */
        private const int MinMove = 50;
        private const int Sides = 30; /* Number of sides for the circular polygons, the more the better */
        private const int MissileBufferEnd = 100; /* Extra distance at the End of the missile*/
        private const int MissileBufferFront = 100; /* Extra distance in Front of the missile*/
        private const int EvadeMissileFrontBuffer = 10;
        private const bool TestOnAllies = false;
        private const bool ShowSkillShotData = false;
        private const int SearchStep = 40;
        private const int DashEvadeBuffer = 100;
        private const int SmoothEvadeBufferT = 400;
        private static readonly List<Vector2> DrawCircles = new List<Vector2>();
        private static readonly List<Vector3> DrawCircles2 = new List<Vector3>();

        /*Constants*/
        private static int hitbox = 65; /* Actually is not a constant*/
        /***********/

        private static readonly Dictionary<String, String> MissileNameToSpellName = new Dictionary<String, String>();
        private static readonly Dictionary<String, SkillShot> SkillShots = new Dictionary<String, SkillShot>();
        private static readonly Dictionary<String, Dash> Dashes = new Dictionary<String, Dash>();
        private static int LastCTick = 0;
        private static bool Evading;
        private static bool CantEvade;
        private static Vector2 EvadePoint;
        private static List<List<Vector2>> ClippedPolygon = new List<List<Vector2>>();

        private static void Main(string[] args)
        {
            Game.OnGameStart += OnGameStart;
            if (Game.Mode == GameMode.Running)
                OnGameStart(new EventArgs());
        }

        private static void OnGameStart(EventArgs args)
        {
            
            hitbox = (int)ObjectManager.Player.BoundingRadius;
            Game.OnGameUpdate += OnTick; /* Gets called every time the game updates*/
            Game.OnGameSendPacket += OnSendPacket; /* Gets called every time the client sends a packet */

            Obj_AI_Base.OnProcessSpellCast += OnProcessSpell; /* Gets called every time a visible unit casts a spell */
            GameObject.OnCreate += OnCreateMissile; /* Gets called when a line missile is created*/
            GameObject.OnDelete += OnDeleteMissile; /* Gets called when a line missile is destroyed/collided */

            Drawing.OnDraw += OnDraw;

            Game.PrintChat("Evade# Loaded");

            /* Supported SkillShot list, probably this will change in the future */

            SkillShot SK;
            /* Aatrox */
            if (Utils.IsChampionPresent("Aatrox", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("AatroxQ",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 600, 650, 2000, 150 + hitbox, 0, false, false, false, false, false));

                /* E */
                SkillShots.Add("AatroxE",
                    new SkillShot(SkillShotType.SKILLSHOT_TRIANGLE, 250, 1075, 1250, 150, 0, true, false, false, false, false));
            }

            /* Ahri */
            if (Utils.IsChampionPresent("Ahri", TestOnAllies))
            {
                /* Q TODO: Take into account the acceleration. */
                SkillShots.Add("AhriOrbofDeception",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1600, 100 + hitbox, 0, true, false, false, true, false));

                SK = new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1600, 100 + hitbox, 0, true, false, false, true, false);
                SK.LineMissileTrackUnits = true;

                SkillShots.Add("AhriOrbReturn", SK);
                MissileNameToSpellName.Add("AhriOrbReturn", "AhriOrbReturn");
                MissileNameToSpellName.Add("AhriOrbMissile", "AhriOrbofDeception");

                /* E */
                SkillShots.Add("AhriSeduce",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1500, 60 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("AhriSeduceMissile", "AhriSeduce");
            }

            if (Utils.IsChampionPresent("Anivia", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("FlashFrost",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 850, 110 + hitbox, 0, false, true, true, true,
                        false));
                MissileNameToSpellName.Add("FlashFrostSpell", "FlashFrost");
            }

            /* Annie */
            if (Utils.IsChampionPresent("Annie", TestOnAllies))
            {
                /*W*/
                SkillShots.Add("Incinerate",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 600, 1, int.MaxValue, 625, 50f * (float)Math.PI / 180, true, false, false, true, false));

                /*R TODO:Detection from fow?*/
                SkillShots.Add("InfernalGuardian",
                               new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 600, int.MaxValue, 251 + hitbox, 0, false, false, true, true, true));
            }

            /* Amumu */
            if (Utils.IsChampionPresent("Amumu", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("BandageToss",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 2000, 130 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("SadMummyBandageToss", "BandageToss");

                /* R */
                SkillShots.Add("CurseoftheSadMummy",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 0, int.MaxValue, 550, 0, true, false, true, true, true));
            }

            /* Ashe */
            if (Utils.IsChampionPresent("Ashe", TestOnAllies))
            {
                /* W 
                SkillShots.Add("Volley", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, 0, true, false, false, false, false));

                SkillShots.Add("Volley1", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, (float)Math.PI / 180 * 9.583f, true, false, false, false, false));
                SkillShots.Add("Volley2", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, (float)Math.PI / 180 * 2 * 9.583f, true, false, false, false, false));
                SkillShots.Add("Volley3", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, (float)Math.PI / 180 * 3 * 9.583f, true, false, false, false, false));

                SkillShots.Add("Volley4", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, -(float)Math.PI / 180 * 9.583f, true, false, false, false, false));
                SkillShots.Add("Volley5", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, -(float)Math.PI / 180 * 2 * 9.583f, true, false, false, false, false));
                SkillShots.Add("Volley6", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, -(float)Math.PI / 180 * 3 * 9.583f, true, false, false, false, false));
                */
                /* R */
                SkillShots.Add("EnchantedCrystalArrow",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 25000, 1600, 130 + hitbox, 0, true, true, true, true, true));
                MissileNameToSpellName.Add("EnchantedCrystalArrow", "EnchantedCrystalArrow");
            }
            /* Brand */
            if (Utils.IsChampionPresent("Brand", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("BrandBlaze",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1600, 60 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("BrandBlazeMissile", "BrandBlaze");

                /* W */
                SkillShots.Add("BrandFissure",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 850, 900, int.MaxValue, 240 + hitbox, 0, false, false, false, true, false));
            }
            
            /* Braum */
            if (Utils.IsChampionPresent("Braum", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("BraumQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 1700, 60 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("BraumQMissile", "BraumQ");

                /* R */
                SkillShots.Add("BraumRWrapper",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 500, 1200, 1400, 115 + hitbox, 0, true, false, true, true, false));
                MissileNameToSpellName.Add("BraumRMissile", "BraumRWrapper");
                
            }

            /* Blitzcrank */
            if (Utils.IsChampionPresent("Blitzcrank", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("RocketGrab",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 1800, 70 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("RocketGrab", "RocketGrabMissile");

                /* R */
                SkillShots.Add("StaticField",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 0, int.MaxValue, 600, 0, true, false, true, false, false));
            }

            /* Caitlyn */
            if (Utils.IsChampionPresent("Caitlyn", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("CaitlynPiltoverPeacemaker",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 625, 1300, 2200, 90 + hitbox, 0, true, false, false, true, false));
                MissileNameToSpellName.Add("CaitlynPiltoverPeacemaker", "CaitlynPiltoverPeacemaker");

                /* E */
                SkillShots.Add("CaitlynEntrapment",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1000, 2000, 80 + hitbox, 0, true, true, false, false, false));
                MissileNameToSpellName.Add("CaitlynEntrapment", "CaitlynEntrapment");
            }

            /*Cassiopeia*/
            if (Utils.IsChampionPresent("Cassiopeia", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("CassiopeiaNoxiousBlast",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 600, 850, int.MaxValue, 150 + hitbox, 0, false, false, false, false, false));

                /*R*/
                SkillShots.Add("CassiopeiaPetrifyingGaze",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 600, 1, int.MaxValue, 825, 80f * (float)Math.PI / 180, true, false, true, true, true));
            }

            /*Chogath*/
            if (Utils.IsChampionPresent("Chogath", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("Rupture",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1200, 950, int.MaxValue, 250 + hitbox, 0, false, false, true, true, false));

                /*W*/
                SkillShots.Add("FeralScream",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 650, 1, int.MaxValue, 650, 2 * 28f * (float)Math.PI / 180, true, false, false, false, false));
            }

            /*Corki*/
            if (Utils.IsChampionPresent("Corki", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("PhosphorusBomb",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 500, 825, 1125, 250 + hitbox, 0, false, false, false, true, false));
                MissileNameToSpellName.Add("PhosphorusBombMissile", "PhosphorusBomb");

                /*R*/
                SkillShots.Add("MissileBarrage",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 200, 1300, 2000, 40 + hitbox, 0, true, true, false, false, false));
                MissileNameToSpellName.Add("MissileBarrageMissile", "MissileBarrage");
                MissileNameToSpellName.Add("MissileBarrageMissile2", "MissileBarrage");
            }

            /* Darius */
            if (Utils.IsChampionPresent("Darius", TestOnAllies))
            {
                /*E*/
                SkillShots.Add("DariusAxeGrabCone",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 600, 1, int.MaxValue, 550, 50f * (float)Math.PI / 180, true, false, true, true, false));
            }

            /* DrMundo*/
            if (Utils.IsChampionPresent("DrMundo", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("InfectedCleaverMissileCast",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 2000, 60 + hitbox, 0, true, true, false, true, false));
                MissileNameToSpellName.Add("InfectedCleaverMissile", "InfectedCleaverMissileCast");
            }

            /* Draven*/
            if (Utils.IsChampionPresent("Draven", TestOnAllies))
            {
                /* E */
                SkillShots.Add("DravenDoubleShot",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1400, 130 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("DravenDoubleShotMissile", "DravenDoubleShot");

                /* R */
                SkillShots.Add("DravenRCast",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 1000, 20000, 2000, 160 + hitbox, 0, true, false, true, true, false));

                MissileNameToSpellName.Add("DravenR", "DravenRCast");
            }

            /* Elise */
            if (Utils.IsChampionPresent("Elise", TestOnAllies))
            {
                /* E */
                SkillShots.Add("EliseHumanE",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1450, 70 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("EliseHumanE", "EliseHumanE");
            }

            /* Evelynn */
            if (Utils.IsChampionPresent("Evelynn", TestOnAllies))
            {
                /* R */
                SkillShots.Add("EvelynnR",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 650, int.MaxValue, 350 + hitbox, 0, false, false, true, true, true));
            }

            /* Ezreal */
            if (Utils.IsChampionPresent("Ezreal", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("EzrealMysticShot",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 2000, 60 + hitbox, 0, true, true, false, false, false));
                MissileNameToSpellName.Add("EzrealMysticShotMissile", "EzrealMysticShot");

                /* W */
                SkillShots.Add("EzrealEssenceFlux",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 1600, 80 + hitbox, 0, true, false, false, false, false));
                MissileNameToSpellName.Add("EzrealEssenceFluxMissile", "EzrealEssenceFlux");

                /* R */
                SkillShots.Add("EzrealTrueshotBarrage",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 1000, 20000, 2000, 160 + hitbox, 0, true, false, true, true, true));
                MissileNameToSpellName.Add("EzrealTrueshotBarrage", "EzrealTrueshotBarrage");
            }

            /* Fizz */
            if (Utils.IsChampionPresent("Fizz", TestOnAllies))
            {
                /* R */
                SkillShots.Add("FizzMarinerDoom",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1300, 1350, 120 + hitbox, 0, false, true, false, false, false));
                MissileNameToSpellName.Add("FizzMarinerDoomMissile", "FizzMarinerDoom");
            }

            /* Galio */
            if (Utils.IsChampionPresent("Galio", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("GalioResoluteSmite",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 900, 1300, 200 + hitbox, 0, false, false, false, true, false));

                /* E 
               SkillShots.Add("EzrealMysticShot",
                   new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1200, 120 + hitbox, 0, true, false, false, false, false));
               MissileNameToSpellName.Add("EzrealMysticShotMissile", "EzrealMysticShot");*/

                /* R */
                SkillShots.Add("GalioIdolOfDurand",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 0, int.MaxValue, 550, 0, true, false, true, true, true));
            }

            /* Gragas */
            if (Utils.IsChampionPresent("Gragas", TestOnAllies))
            {
                /* Q */
                SK = new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1100, 1300, 275 + hitbox, 0, false, false, false, false, false);
                SK.ExtraDuration = 4200;
                SkillShots.Add("GragasQ", SK);
                MissileNameToSpellName.Add("GragasQMissile", "GragasQ");


                /* E */
                SkillShots.Add("GragasE",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 700, 1200, 50 + hitbox, 0, false, true, true, true, false));

                /* R */
                SkillShots.Add("GragasR",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 700, 1050, int.MaxValue, 375 + hitbox, 0, false, false, false, true, true));
                /* TODO: Add detection from fow */
                MissileNameToSpellName.Add("GragasR", "GragasR");//Not tested
            }

            /* Donger */
            if (Utils.IsChampionPresent("Heimerdinger", TestOnAllies))
            {
                /* E */
                SkillShots.Add("HeimerdingerE",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 925, 1200, 100 + hitbox, 0, false, false, false, false, false));

                MissileNameToSpellName.Add("heimerdingerespell", "HeimerdingerE");
            }

            /* Irelia */
            if (Utils.IsChampionPresent("Irelia", TestOnAllies))
            {

                SkillShots.Add("IreliaTranscendentBlades",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 1200, 1600, 0 + hitbox, 0, true, false, false, false,
                        false));
                MissileNameToSpellName.Add("IreliaTranscendentBladesSpell", "IreliaTranscendentBlades");
            }

            /* Jarvan */
            if (Utils.IsChampionPresent("JarvanIV", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("JarvanIVDragonStrike",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1450, 70 + hitbox, 0, true, false, false, true, false));

                /* E */
                SkillShots.Add("JarvanIVDemacianStandard",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 500, 860, int.MaxValue, 175 + hitbox, 0, false, false, false, false, false));
            }

            /* Jinx */
            if (Utils.IsChampionPresent("Jinx", TestOnAllies))
            {
                /* W TODO: Detect the animation from fow*/
                SkillShots.Add("JinxW",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 600, 1500, 3300, 60 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("JinxWMissile", "JinxW");

                /* R TODO: Take into account the speed change*/
                SkillShots.Add("JinxRWrapper",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 600, 20000, 1700, 140 + hitbox, 0, true, true, true, true, true));
                MissileNameToSpellName.Add("JinxR", "JinxRWrapper");
            }

            /* Karma */
            if (Utils.IsChampionPresent("Karma", TestOnAllies))
            {

                SkillShots.Add("KarmaQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 950, 1700, 60 + hitbox, 0, true, true, true, true,
                        false));
                MissileNameToSpellName.Add("KarmaQMissile", "Karma");
                MissileNameToSpellName.Add("KarmaQMissileMantra", "Karma");//add the aoe circle?
            }

            /*Karthus*/
            if (Utils.IsChampionPresent("Karthus", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("LayWaste",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 625, 875, int.MaxValue, 160 + hitbox, 0, false, false, false, false, false));
            }

            /* Kassadin */
            if (Utils.IsChampionPresent("Kassadin", TestOnAllies))
            {
                /*E*/
                SkillShots.Add("ForcePulse",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 700, 1, int.MaxValue, 650, 2 * 39f * (float)Math.PI / 180, true, false, false, false, false));

                /*R*/
                SkillShots.Add("RiftWalk",
                               new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 700, int.MaxValue, 270 + hitbox, 0, false, false, false, false, false));
            }

            /* Kennen */
            if (Utils.IsChampionPresent("Kennen", TestOnAllies))
            {

                SkillShots.Add("KennenShurikenHurlMissile1",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1050, 1700, 50 + hitbox, 0, true, true, false, false,
                        false));
                MissileNameToSpellName.Add("KennenShurikenHurlMissile1", "KennenShurikenHurlMissile1");
            }

            /* Khazix */
            if (Utils.IsChampionPresent("Khazix", TestOnAllies))//TODO:Check
            {
                /*W*/
                SkillShots.Add("KhazixW",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1025, 1700, 70 + hitbox, 0, true, true, false, false,
                        false));
                MissileNameToSpellName.Add("KhazixWMissile", "KhazixW");

                /*W*/
                SkillShots.Add("khazixwlong",
                   new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1025, 1700, 70 + hitbox, 0, true, true, false, false,
                        false));

                /*E*/
                SkillShots.Add("KhazixE",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 600, 1500, 300 + hitbox, 0, false, false, false, false,
                        false));

                MissileNameToSpellName.Add("KhazixEInvisMissile", "KhazixE");
            }

            /* KogMaw */
            if (Utils.IsChampionPresent("KogMaw", TestOnAllies))
            {
                /*Q*/
                SkillShots.Add("KogMawQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1650, 70 + hitbox, 0, true, true, false, false,
                        false));
                MissileNameToSpellName.Add("KogMawQMis", "KogMawQ");

                /*E*/
                SkillShots.Add("KogMawVoidOoze",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1500, 1400, 120 + hitbox, 0, true, false, false, false,
                        false));
                MissileNameToSpellName.Add("KogMawVoidOozeMissile", "KogMawVoidOoze");

                /*R*/
                SkillShots.Add("KogMawLivingArtillery",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1000, 1800, int.MaxValue, 225 + hitbox, 0, false, false, false, false, false));
            }

            /* Leblanc */
            if (Utils.IsChampionPresent("Leblanc", TestOnAllies))
            {
                /* W */
                SkillShots.Add("LeblancSlide",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 0, 600, 1500, 220 + hitbox, 0, false, false, false, true, false));

                /* W */
                SkillShots.Add("LeblancSlideM",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 0, 600, 1500, 220 + hitbox, 0, false, false, false, true, false));

                /* E */
                SkillShots.Add("LeblancSoulShackle",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 950, 1600, 70 + hitbox, 0, true, true, true, true, false));
                /* E */
                SkillShots.Add("LeblancSoulShackleM",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 950, 1600, 70 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("LeblancSoulShackle", "LeblancSoulShackle");
                MissileNameToSpellName.Add("LeblancSoulShackleM", "LeblancSoulShackleM");
            }

            /* LeeSin */
            if (Utils.IsChampionPresent("LeeSin", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("BlindMonkQOne",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1800, 60 + hitbox, 0, true, true, true, true, false));

                MissileNameToSpellName.Add("BlindMonkQOne", "BlindMonkQOne");
            }

            /* Leona */
            if (Utils.IsChampionPresent("Leona", TestOnAllies))
            {
                /* E */
                SkillShots.Add("LeonaZenithBlade",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 900, 2000, 90 + hitbox, 0, true, false, true, true, false));
                MissileNameToSpellName.Add("LeonaZenithBladeMissile", "LeonaZenithBlade");

                /* R TODO: fow detection*/
                SkillShots.Add("LeonaSolarFlare",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1000, 1200, int.MaxValue, 120 + hitbox, 0, false, false, true, true, true));
            }

            /* Lux */
            if (Utils.IsChampionPresent("Lux", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("LuxLightBinding",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1300, 1200, 70 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("LuxLightBindingMis", "LuxLightBinding");

                /* E Not dangerous enough to have it enabled by default, TODO: Delete when the object dissapears*/
                SK = new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1100, 1300, 275 + hitbox, 0, false, false, false, false, false);
                SK.ExtraDuration = 5000;
                SkillShots.Add("LuxLightStrikeKugel", SK);
                //MissileNameToSpellName.Add("LuxLightBindingMis", "LuxLightBinding");

                /* R */
                SkillShots.Add("LuxMaliceCannon",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 1350, 3500, int.MaxValue, 190 + hitbox, 0, true, false, true, true, false));
                /* TODO: Add detection from fow */
            }

            /* Malphite */
            if (Utils.IsChampionPresent("Malphite", TestOnAllies))
            {
                /* R TODO: fow detection*/
                SkillShots.Add("UFSlash",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1000, 1500, 270 + hitbox, 0, false, false, true, true, true));
            }

            /* Malzahar */
            if (Utils.IsChampionPresent("Malzahar", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("AlZaharCalloftheVoidS1", new SkillShot(SkillShotType.SKILLSHOT_LINE, 700, 700, 1600, 85 + hitbox, 0, true, false, true, true, false));
                SkillShots.Add("AlZaharCalloftheVoidS2", new SkillShot(SkillShotType.SKILLSHOT_LINE, 700, 700, 1600, 85 + hitbox, 0, true, false, true, true, false));

                SkillShots.Add("AlZaharCalloftheVoid", new SkillShot(SkillShotType.SKILLSHOT_NONE, 600, 90, 1600, 85 + hitbox, 0, false, false, true, true, false));
            }

            /* Mordekaiser */
            if (Utils.IsChampionPresent("Mordekaiser", TestOnAllies))
            {
                /*E*/
                SkillShots.Add("MordekaiserSyphonOfDestruction",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 600, 1, int.MaxValue, 700, 50f * (float)Math.PI / 180, true,
                        false, false, true, false));
            }

            /* Morgana */
            if (Utils.IsChampionPresent("Morgana", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("DarkBindingMissile",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1300, 1200, 70 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("DarkBindingMissile", "DarkBindingMissile");
            }

            /* Nidalee */
            if (Utils.IsChampionPresent("Nidalee", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("JavelinToss",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1500, 1300, 60 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("JavelinToss", "JavelinToss");
            }

            /* Nautilus */
            if (Utils.IsChampionPresent("Nautilus", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("NautilusAnchorDrag",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 2000, 90 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("NautilusAnchorDragMissile", "NautilusAnchorDrag");
            }

            /* Olaf */
            if (Utils.IsChampionPresent("Olaf", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("OlafAxeThrowCast",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1600, 90 + hitbox, 0, false, true, true, true, false));
                MissileNameToSpellName.Add("olafaxethrow", "OlafAxeThrowCast");
            }

            /* Orianna TODO: Add E and R*/
            if (Utils.IsChampionPresent("Orianna", TestOnAllies) || Utils.IsChampionPresent("OriannaNoBall", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("NotRealNameOrianna",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 1500, 1200, 80 + hitbox, 0, false, false, false, false, false));//Not real spell name since the delay is 0 and the missile gives us the start position.
                MissileNameToSpellName.Add("orianaizuna", "NotRealNameOrianna");

                /* W */
                SK = new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1, int.MaxValue, 255 + hitbox, 0, true, false, false, false, false);
                SK.FromObject = "TheDoomBall";
                SkillShots.Add("OrianaDissonanceCommand", SK);

                /* R */
                SK = new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 700, 1, int.MaxValue, 410 + hitbox, 0, true, false, true, true, true);
                SK.FromObject = "TheDoomBall";
                SkillShots.Add("OrianaDetonateCommand", SK);
            }

            /*Quinn*/
            if (Utils.IsChampionPresent("Quinn", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("QuinnQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 1550, 80 + hitbox, 0, true, true, false, true, false));
                MissileNameToSpellName.Add("QuinnQMissile", "QuinnQ");
            }

            /* Rengar TODO, check*/
            if (Utils.IsChampionPresent("Rengar", TestOnAllies))
            {
                /* E */
                SkillShots.Add("RengarE",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1500, 70 + hitbox, 0, true, true, false, true, false));
                MissileNameToSpellName.Add("RengarEFinal", "RengarE");
                MissileNameToSpellName.Add("RengarEFinalMAX", "RengarE");
            }

            /*Riven*/
            if (Utils.IsChampionPresent("Riven", TestOnAllies))
            {
                /*R*/
                SkillShots.Add("rivenizunablade",
                    new SkillShot(SkillShotType.SKILLSHOT_CONE, 500, 1, 2200, 1100, 45f * (float)Math.PI / 180, true, false, true, true, false));
            }

            /* Rumble */
            if (Utils.IsChampionPresent("Rumble", TestOnAllies))
            {
                /* E */
                SkillShots.Add("RumbleGrenade",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 950, 2000, 60 + hitbox, 0, true, true, false, true, false));
                MissileNameToSpellName.Add("RumbleGrenadeMissile", "RumbleGrenade");
            }

            /* Sivir */
            if (Utils.IsChampionPresent("Sivir", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("SivirQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1175, 1350, 90 + hitbox, 0, true, false, true, true, false));

                SK = new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1175, 1350, 100 + hitbox, 0, true, false, true, false, false);
                SK.LineMissileTrackUnits = true;

                SkillShots.Add("SivirQReturn", SK);
                MissileNameToSpellName.Add("SivirQMissileReturn", "SivirQReturn");
                MissileNameToSpellName.Add("SivirQMissile", "SivirQ");
            }

            /*Skarner*/
            if (Utils.IsChampionPresent("Skarner", TestOnAllies))
            {
                SkillShots.Add("SkarnerFracture", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1500, 70 + hitbox, 0, true, false, false, false, false));
                MissileNameToSpellName.Add("SkarnerFractureMissile", "SkarnerFracture");

            }

            /* Sona */
            if (Utils.IsChampionPresent("Sona", TestOnAllies))
            {
                SkillShots.Add("SonaCrescendo",
                        new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 2400, 140 + hitbox, 0, true, false, true, true, true));
                MissileNameToSpellName.Add("SonaCrescendo", "SonaCrescendo");
            }

            /* Syndra */
            if (Utils.IsChampionPresent("Syndra", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("SyndraQ",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 600, 800, int.MaxValue, 150 + hitbox, 0, false, false,
                        false, false, false));

                /* W */
                SkillShots.Add("syndrawcast",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 900, 950, int.MaxValue, 210 + hitbox, 0, false, false,
                        false, false, false));

                /* E TODO*/
            }

            /*Shen*/
            if (Utils.IsChampionPresent("Shen", TestOnAllies))
            {
                SkillShots.Add("ShenShadowDash", new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 650, 1600, 50 + hitbox, 0, false, false, true, true, false));
            }

            /*Shyvana*/
            if (Utils.IsChampionPresent("Shyvana", TestOnAllies))
            {
                SkillShots.Add("ShyvanaFireball", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 950, 1700, 60 + hitbox, 0, true, false, false, false, false));
                MissileNameToSpellName.Add("ShyvanaFireballMissile", "ShyvanaFireball");

                SkillShots.Add("ShyvanaTransformCast", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1500, 150 + hitbox, 0, true, false, false, false, false));
            }

            /* Swain TODO: CHECK*/
            if (Utils.IsChampionPresent("Swain", TestOnAllies))
            {
                SkillShots.Add("SwainShadowGrasp",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 900, 900, int.MaxValue, 180 + hitbox, 0, false, false, true, true, false));
            }

            /*Tryndamere*/
            if (Utils.IsChampionPresent("Tryndamere", TestOnAllies))
            {
                SkillShots.Add("slashCast", new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 660, 1300, 93 + hitbox, 0, false, false, false, false, false));
            }

            /* Tristana */
            if (Utils.IsChampionPresent("Tristana", TestOnAllies))
            {
                /* W*/
                SkillShots.Add("RocketJump",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 500, 900, 1500, 270 + hitbox, 0, false, false, true, false, false));
            }

            /* Twitch */
            if (Utils.IsChampionPresent("Twitch", TestOnAllies))
            {
                /* W*/
                SkillShots.Add("TwitchVenomCask",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 900, 1400, 275 + hitbox, 0, false, false, true, false, false));
                MissileNameToSpellName.Add("TwitchVenomCaskMissile", "TwitchVenomCask");
            }

            /* Twisted Fate */
            if (Utils.IsChampionPresent("TwistedFate", TestOnAllies))
            {
                SkillShots.Add("WildCards", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1450, 1000, 40 + hitbox, 0, true, false, false, false, false));

                SkillShots.Add("WildCards1", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1450, 1000, 40 + hitbox, 28 * (float)Math.PI / 180, true, false, false, false, false));
                SkillShots.Add("WildCards2", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1450, 1000, 40 + hitbox, -28 * (float)Math.PI / 180, true, false, false, false, false));
                MissileNameToSpellName.Add("SealFateMissile", "WildCards");
            }

            /* Thresh */
            if (Utils.IsChampionPresent("Thresh", TestOnAllies))
            {
                SkillShots.Add("ThreshQ", new SkillShot(SkillShotType.SKILLSHOT_LINE, 500, 1100, 1900, 70 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("ThreshQMissile", "ThreshQ");

                SkillShots.Add("ThreshE", new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1075, 2000, 110 + hitbox, 0, true, false, true, true, false));
            }

            /* Urgot*/
            if (Utils.IsChampionPresent("Urgot", TestOnAllies))
            {
                /* Q */
                SkillShots.Add("UrgotHeatseekingLineMissile", new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1000, 1600, 60 + hitbox, 0, true, true, false, false, false));
                MissileNameToSpellName.Add("UrgotHeatseekingLineMissile", "UrgotHeatseekingLineMissile");

                /* W */
                SkillShots.Add("UrgotPlasmaGrenade",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1100, 1500, 210 + hitbox, 0, false, false, false, false, false));
                MissileNameToSpellName.Add("UrgotPlasmaGrenadeBoom", "UrgotPlasmaGrenade");
            }

            /*Varus*/
            if (Utils.IsChampionPresent("Varus", TestOnAllies))
            {

                /*Q*/
                SkillShots.Add("VarusQ!",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1800, 1900, 70 + hitbox, 0, true, false, true,
                        true, false));
                MissileNameToSpellName.Add("VarusQMissile", "VarusQ!");
                /* E*/
                SkillShots.Add("VarusE",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1000, 925, 1500, 235 + hitbox, 0, false, false, false, false, false));
                MissileNameToSpellName.Add("VarusEMissile", "VarusE");

                /*R*/
                SkillShots.Add("VarusR",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1950, 100 + hitbox, 0, true, true, true,
                        true, true));
                MissileNameToSpellName.Add("VarusRMissile", "VarusR");

            }

            /*Velkoz*/
            if (Utils.IsChampionPresent("Velkoz", TestOnAllies))
            {

                /*Q*/
                SkillShots.Add("VelkozQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1300, 50 + hitbox, 0, true, true, true,
                        false, false));
                MissileNameToSpellName.Add("VelkozQMissile", "VelkozQ");

                SkillShots.Add("VelkozQ21",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 900, 2100, 45 + hitbox, 0, true, true, true,
                        true, false));
                SkillShots.Add("VelkozQ22",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 900, 2100, 45 + hitbox, 0, true, true, true,
                        true, false));
                //MissileNameToSpellName.Add("VelkozQMissileSplit", "VelkozQ2");


                /* W*/
                SK = new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1700, 65 + hitbox, 0, true, false, false, false, false);
                SkillShots.Add("VelkozW",SK);
                MissileNameToSpellName.Add("VelkozWMissile", "VelkozW");

                /*E*/
                SkillShots.Add("VelkozE",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 800, 1500, 225, 0, false, false, true,
                        true, false));
                MissileNameToSpellName.Add("VarusRMissile", "VarusR");

            }

            /* Veigar */
            if (Utils.IsChampionPresent("Veigar", TestOnAllies))
            {
                /*W*/
                SkillShots.Add("VeigarDarkMatter",
            new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1350, 900, int.MaxValue, 225 + hitbox, 0, false, false, true, false, false));

                /*E*/
                SK = new SkillShot(SkillShotType.SKILLSHOT_RING, 250, 600, int.MaxValue, 350, 0, false, false, true, false, false);
                SK.ExtraDuration = 3000;
                SkillShots.Add("VeigarEventHorizon", SK);
            }

            /* Vi TODO*/
            if (Utils.IsChampionPresent("Vi", TestOnAllies))
            {
                /*SkillShots.Add("ViQMissile",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1150, 1500, 90 + hitbox, 0, true, false, false, false,
                        false));*/
            }

            /* Xerath */
            if (Utils.IsChampionPresent("Xerath", TestOnAllies))
            {

                SkillShots.Add("xeratharcanopulse2",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 600, 1600, int.MaxValue, 80 + hitbox, 0,
                        true, false, false, false,
                        false));

                SkillShots.Add("XerathArcaneBarrage2",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 900, 1000, int.MaxValue, 200 + hitbox, 0,
                        false, false, false, false,
                        false));

                SkillShots.Add("XerathMageSpear", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1400, 60 + hitbox, 0, true, true, true, true, false));
                MissileNameToSpellName.Add("XerathMageSpearMissile", "XerathMageSpear");

                SkillShots.Add("xerathrmissilewrapper",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 600, 5600, int.MaxValue, 110 + hitbox, 0,
                        false, false, false, false,
                        false));
            }

            /* Yasuo */
            if (Utils.IsChampionPresent("Yasuo", TestOnAllies))
            {

                SkillShots.Add("yasuoq",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 500, 520, int.MaxValue, 15 + hitbox, (float)Math.PI, true, false, false, false,
                        false));//not sure why its rotated :<

                SkillShots.Add("yasuoq2",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 500, 520, int.MaxValue, 15 + hitbox, (float)Math.PI, true, false, false, false,
                        false));//not sure why its rotated :<

                SkillShots.Add("yasuoq3w",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1150, 1500, 90 + hitbox, 0, true, false, false, false,
                        false));
            }

            /*Zac*/
            if (Utils.IsChampionPresent("Zac", TestOnAllies))
            {
                SkillShots.Add("ZacQ",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 500, 550, int.MaxValue, 120 + hitbox, 0, true, false, false,
                        false, false));
            }

            /*Zed*/
            if (Utils.IsChampionPresent("Zed", TestOnAllies))
            {
                SkillShots.Add("ZedShuriken",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 925, 1700, 50 + hitbox, 0, true, false, false,
                        false, false));
                MissileNameToSpellName.Add("zedshurikenmisone", "ZedShuriken");
                MissileNameToSpellName.Add("zedshurikenmistwo", "ZedShuriken");
            }

            /*Zyra*/
            if (Utils.IsChampionPresent("Zyra", TestOnAllies))
            {
                /* Q*/
                SkillShots.Add("ZyraQFissure",
                    new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1000, 800, int.MaxValue, 220 + hitbox, 0, false, false, false, false, false));
                MissileNameToSpellName.Add("ZyraQFissure", "ZyraQFissure");

                /* E */
                SkillShots.Add("ZyraGraspingRoots",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1150, 1150, 70 + hitbox, 0, true, false, true,
                        false, false));
                MissileNameToSpellName.Add("ZyraGraspingRoots", "ZyraGraspingRoots");

                /* Passive */
                SkillShots.Add("zyrapassivedeathmanager",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 500, 1474, 2000, 60 + hitbox, 0, true, false, false,
                        false, false));
                MissileNameToSpellName.Add("zyrapassivedeathmanager", "zyrapassivedeathmanager");
            }

            /* Dash data */
            Dashes.Add("Vayne",
                new Dash(250, 900, 300, true, SpellSlot.Q, false, true, false, false, false, false, false));
            Dashes.Add("Ezreal",
                new Dash(250, float.MaxValue, 475, false, SpellSlot.E, true, true, false, false, false, false, false));

            /*Add flash as dash if available*/
            foreach (var spell in ObjectManager.Player.SummonerSpellbook.Spells)
            {
                if (spell.Name == "SummonerFlash")
                {
                    Dashes.Add("Flash", new Dash(0, float.MaxValue, 400, false, spell.Slot, true, true, false, false, false, false, false));
                }
            }

            foreach (var skillshot in SkillShots)
                SkillShots[skillshot.Key].name = skillshot.Key;

            foreach (var dash in Dashes)
                Dashes[dash.Key].Name = dash.Key;

            Game.OnWndProc += onwndmsg;
        }

        //* New skillshots detection: *//
        private static void onwndmsg(WndEventArgs args)
        {
            if (args.Msg == 0x100)
            {
                if (args.WParam == 74)
                {
                    AddSkillShot("WildCards", ObjectManager.Player, Utils.To2D(Game.CursorPos), Utils.To2D(ObjectManager.Player.ServerPosition), Environment.TickCount, true);
                }
                if (args.WParam == 75)
                {
                    AddSkillShot("SivirQ", ObjectManager.Player, new Vector2(Game.CursorPos.X + 700, Game.CursorPos.Y + 500), Utils.To2D(ObjectManager.Player.ServerPosition), Environment.TickCount, true);
                }
                if (args.WParam == 32)
                {
                    ConfigDodgeOnlyDangerous = true;
                }
            }

            if (args.Msg == 0x101)
            {
                if (args.WParam == 32)
                {
                    ConfigDodgeOnlyDangerous = false;
                }

            }
        }

        private static void AddSkillShot(string Name, Obj_AI_Base unit, Vector2 StartPos, Vector2 EndPos, int StartT, bool FromProcessSpell)
        {
            if (!unit.IsEnemy && !TestOnAllies)
                return;

            /* Don't add it if it's already added */
            if (SkillShots[Name].IsActive(0))
                return;

            if (Name == "AlZaharCalloftheVoid")
            {
                var hDir = Utils.perpendicular(EndPos - StartPos);
                hDir.Normalize();

                AddSkillShot("AlZaharCalloftheVoidS1", unit, EndPos + 350 * hDir, EndPos - 350 * hDir, StartT, true);
                AddSkillShot("AlZaharCalloftheVoidS2", unit, EndPos - 350 * hDir, EndPos + 350 * hDir, StartT, true);
            }

            if (Name == "ThreshE")
            {
                var hDir = (EndPos - StartPos);
                hDir.Normalize();
                StartPos = StartPos - hDir * SkillShots["ThreshE"].Range/2;
            }
            if (FromProcessSpell)
                for (int i = 1; i < 9; i++)
                {
                    if (SkillShots.ContainsKey(Name + i))
                    {
                        AddSkillShot(Name + i, unit, StartPos, EndPos, StartT, true);
                    }
                }

            if (Vector3.Distance(unit.ServerPosition, ObjectManager.Player.ServerPosition) <=
                (SkillShots[Name].Range + 1500)) //Probably not needed since L# OP
            {
                SkillShots[Name].Caster = unit;
                SkillShots[Name].StartT = StartT;
                SkillShots[Name].StartPos = StartPos;


                Vector2 Direction = EndPos - StartPos;
                Direction.Normalize();


                if (((Vector2.Distance(Utils.To2D(unit.ServerPosition), EndPos) >
                     SkillShots[Name].Range) || SkillShots[Name].FixedRange) && FromProcessSpell)
                {
                    SkillShots[Name].EndPos = StartPos +
                                              SkillShots[Name].Range * Direction;
                }
                else
                {
                    SkillShots[Name].EndPos = EndPos;
                }

                SkillShots[Name].OnBeingAdded();

                /* Refresh the evade status when adding a skillshot */
                if (Evading)
                    Evading = false;

                OnTick(new EventArgs()); /* Recalculate stuff */
            }
        }

        private static void OnProcessSpell(Obj_AI_Base unit, GameObjectProcessSpellCastEventArgs Spell)
        {
            
            /*Remove Lux's E */
            if (Spell.SData.Name == "luxlightstriketoggle" && (unit.IsEnemy || TestOnAllies) &&
                SkillShots.ContainsKey("LuxLightStrikeKugel"))
                SkillShots["LuxLightStrikeKugel"].StartT = 0;

            /*Remove Gragas's Q */
            if (Spell.SData.Name == "GragasQToggle" && (unit.IsEnemy || TestOnAllies) &&
                SkillShots.ContainsKey("GragasQ"))
                SkillShots["GragasQ"].StartT = 0;


            if (ShowSkillShotData && unit.IsMe)
            {
                string InfoTxt = "<b>OnProcessSpell:</b> ";
                InfoTxt += " Name: " + Spell.SData.Name;
                InfoTxt += " CastRange: " + Spell.SData.CastRange[0];
                InfoTxt += " CastRadius: " + Spell.SData.CastRadius[0];
                InfoTxt += " Linewidth: " + Spell.SData.LineWidth;
                InfoTxt += " CastTime: " + Spell.SData.SpellCastTime;
                InfoTxt += " TotalTime: " + Spell.SData.SpellTotalTime;
                InfoTxt += " CastFrame: " + (Spell.SData.CastFrame / 30);
                InfoTxt += " Speed: " + Spell.SData.MissileSpeed;
                InfoTxt += " Hash: " + Spell.SData.Hash;
                Game.PrintChat(InfoTxt);
            }

            if (SkillShots.ContainsKey(Spell.SData.Name))
            {
                if (Spell.SData.Name == "UFSlash")
                    SkillShots["UFSlash"].MissileSpeed = 1500 + (int)unit.MoveSpeed;

                Vector2 StartPos = new Vector2();

                if (SkillShots[Spell.SData.Name].FromObject != "")
                {
                    Game.PrintChat(SkillShots[Spell.SData.Name].FromObject);
                    foreach (var o in ObjectManager.Get<Obj_AI_Base>())
                    {
                        if (o.Name == SkillShots[Spell.SData.Name].FromObject && (o.IsEnemy || TestOnAllies))
                            StartPos = Utils.To2D(o.Position);
                    }
                }
                else
                {
                    StartPos = Utils.To2D(unit.ServerPosition);
                }

                if (Utils.IsValidVector2(StartPos))
                    AddSkillShot(Spell.SData.Name, unit, StartPos, Utils.To2D(Spell.End),
                    Environment.TickCount - Game.Ping / 2, true);
            }

          
        }

        /* For skillshot detection when they are from fow*/

        private static void OnCreateMissile(GameObject sender, EventArgs args)
        {
            return;
            if (sender is Obj_SpellMissile)
            {
                var obj = (Obj_SpellMissile)sender;

                if (ShowSkillShotData && obj.SpellCaster.IsMe)
                {
                    string InfoTxt = "<b>OnCreateMissile:</b> ";
                    InfoTxt += " Name: " + obj.SData.Name;
                    InfoTxt += " CastRange: " + obj.SData.CastRange[0];
                    InfoTxt += " CastRadius: " + obj.SData.CastRadius[0];
                    InfoTxt += " Linewidth: " + obj.SData.LineWidth;
                    InfoTxt += " CastTime: " + obj.SData.SpellCastTime;
                    InfoTxt += " TotalTime: " + obj.SData.SpellTotalTime;
                    InfoTxt += " CastFrame: " + (obj.SData.CastFrame / 30);
                    InfoTxt += " Speed: " + obj.SData.MissileSpeed;
                    InfoTxt += " Hash: " + obj.SData.Hash;
                    Game.PrintChat(InfoTxt);
                }
              
                if (MissileNameToSpellName.ContainsKey(obj.SData.Name))
                {
                    Game.PrintChat("FOW");

                    Vector3Time[] path = obj.GetPath(0.001f);
                    Vector2 MissilePos = Utils.To2D(obj.Position);
                    Vector2 EndPoint = new Vector2();
                    Game.PrintChat("FOW2");
                    foreach (var p in path)
                    {
                        EndPoint = Utils.To2D(p.Position);

                        Game.PrintChat(p.Position.X + " " + p.Position.Y);
                    }

                    EndPoint = Utils.To2D(Game.CursorPos);
                    if (Utils.IsValidVector2(EndPoint))
                    {
                        AddSkillShot(MissileNameToSpellName[obj.SData.Name], (Obj_AI_Base)obj.SpellCaster, MissilePos, EndPoint,
                            (int)Vector2.Distance(MissilePos, Utils.To2D((obj.SpellCaster).Position)) /
                            SkillShots[MissileNameToSpellName[obj.SData.Name]].MissileSpeed - Game.Ping / 2 + Environment.TickCount - SkillShots[MissileNameToSpellName[obj.SData.Name]].Delay, false);
                    }
                }
              
                
            }
        }

        private static void OnDeleteMissile(GameObject sender, EventArgs args)
        {
            if (sender is Obj_SpellMissile)
            {
                var obj = (Obj_SpellMissile)sender;

                if (!obj.IsEnemy && !TestOnAllies)
                    return;

                if (MissileNameToSpellName.ContainsKey(obj.SData.Name))
                {
                    if (SkillShots[MissileNameToSpellName[obj.SData.Name]].collision)
                    {
                        SkillShots[MissileNameToSpellName[obj.SData.Name]].StartT = 0;
                        SkillShots[MissileNameToSpellName[obj.SData.Name]].OnCollide(obj.Position);
                    }
                }
            }
        }

        private static void OnTick(EventArgs args)
        {
            if (!ConfigEvadingEnabled)
                return;

            CantEvade = false;
            ClippedPolygon = GetClippedPolygons(); /* make the polygon each tick*/

            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    entry.Value.OnTick();
                }
            }

            if (Evading)
            {
                OnEvadingChecks();
            }
            else
            {
                List<Vector2> MyWayPoints = Utils.GetWaypoints(ObjectManager.Player);
                /* Check if its safe to follow the current waypoints */
                ProcessPath(MyWayPoints, false);
            }
        }

        private static void OnEvadingChecks()
        {
            if (IsSafe(Utils.To2D(ObjectManager.Player.ServerPosition))
                /*&& (Utils.GetDistanceToPolygons(GetClippedPolygons(), Utils.To2D(ObjectManager.Player.ServerPosition)) >= 5) */)
            {
                //Stop evading
                Evading = false;
                //Game.PrintChat("Safe point reached.");
            }
            else
            {
                if (!IsSafe(EvadePoint))
                {
                    Evading = false;
                    return;
                }

                List<Vector2> myWaypoints = Utils.GetWaypoints(ObjectManager.Player);
                if (Vector2.Distance(myWaypoints[myWaypoints.Count - 1], EvadePoint) > 4)
                    Utils.SendMoveToPacket(EvadePoint);
                //Evading = false; To change the direction while evading.
            }
        }


        private static bool EvadeDashing(Dash dash)
        {
            if (dash.Name != "Flash" && ObjectManager.Player.Spellbook.CanUseSpell(dash.Slot) != SpellState.Ready)
            {
                return false;
            }
            if (dash.Name == "Flash" && ObjectManager.Player.SummonerSpellbook.CanUseSpell(dash.Slot) != SpellState.Ready)
            {
                return false;
            }
            List<List<Vector2>> Polygons = ClippedPolygon;
            List<Vector2> MyWaypoints = Utils.GetWaypoints(ObjectManager.Player);
            Vector2 from = MyWaypoints[MyWaypoints.Count - 1];


            if (dash.FixedRange)
            {
                //Scan in circle
                var Candidates = new List<Vector2>();

                for (int i = 0; i < 30; i++)
                {
                    float angle = i * 2 * (float)Math.PI / 30;
                    var point =
                        new Vector2(ObjectManager.Player.ServerPosition.X + dash.MaxRange * (float)Math.Cos(angle),
                            ObjectManager.Player.ServerPosition.Y + dash.MaxRange * (float)Math.Sin(angle));

                    var newpath = new List<Vector2>();
                    newpath.Add(Utils.To2D(ObjectManager.Player.ServerPosition));
                    newpath.Add(new Vector2(point.X, point.Y));

                    if (!dash.IsBlink && IsSafeEvadePath(newpath, dash.Speed, dash.Delay, true, true))
                    {
                        Candidates.Add(point);
                    }
                }

                if (Candidates.Count > 0)
                {
                    Vector2 DashPos = Utils.GetClosestVector(from, Candidates);
                    if (dash.Name == "Flash")
                    {
                        ObjectManager.Player.SummonerSpellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                    }
                    else
                    {
                        ObjectManager.Player.Spellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                    }
                    return true;
                }

                return false;
            }
            if (!dash.DASH_SKILLSHOT)
            {
                List<Obj_AI_Base> JumpObjects = dash.GetPosibleEvadeTargets();
                var ValidJumpObjects = new List<Obj_AI_Base>();

                foreach (Obj_AI_Base obj in JumpObjects)
                {
                    var newpath = new List<Vector2>();
                    newpath.Add(Utils.To2D(ObjectManager.Player.ServerPosition));
                    newpath.Add(new Vector2(obj.ServerPosition.X, obj.ServerPosition.Y));

                    if (dash.IsBlink && IsSafeToBlink(Utils.To2D(obj.ServerPosition), dash.Delay)
                        || !dash.IsBlink && IsSafeEvadePath(newpath, dash.Speed, dash.Delay, true, true)
                        )
                    {
                        ValidJumpObjects.Add(obj);
                    }
                }

                if (ValidJumpObjects.Count > 0)
                {
                    /* Jump to the closest object GetClosestUnit(from, uList)*/
                    return true;
                }
                return false;
            }
            if (dash.DASH_SKILLSHOT)
            {
                var Candidates = new List<Vector2>();

                /* Greedy search*/
                foreach (var Polygon in Polygons)
                {
                    for (int i = 0; i < Polygon.Count; i++)
                    {
                        Vector2 A = Polygon[i];
                        Vector2 B = Polygon[(i == Polygon.Count - 1) ? 0 : (i + 1)];
                        Vector2 Dir = (B - A);
                        Dir.Normalize();
                        float dist = Vector2.Distance(A, B);
                        int C = Math.Min(Math.Max(3, (int)(dist / SearchStep) + 1), 20);
                        for (int j = 0; j < C; j++)
                        {
                            Vector2 Candidate = A + j * Dir * dist / C;

                            var newpath = new List<Vector2>();
                            newpath.Add(Utils.To2D(ObjectManager.Player.ServerPosition));

                            Vector2 PDirection = Utils.perpendicular(A - B);
                            PDirection.Normalize();

                            /* Maybe offset the polygon in the future */
                            Vector2 test = Candidate - ExtraW * PDirection;

                            if (IsSafe(test))
                            {
                                Candidate = (Candidate - ExtraW * PDirection);
                                //Evade ExtraW units after exiting the polygon to avoid roundings
                            }
                            else
                            {
                                Candidate = (Candidate + ExtraW * PDirection);
                                //Stop ExtraW units after exiting the polygon to avoid roundings.
                            }

                            newpath.Add(Candidate);

                            if ((Vector2.DistanceSquared(Utils.To2D(ObjectManager.Player.ServerPosition), Candidate) <=
                                 dash.MaxRange * dash.MaxRange) &&
                                (dash.IsBlink && IsSafeToBlink(Candidate, dash.Delay) ||
                                 !dash.IsBlink && IsSafeEvadePath(newpath, dash.Speed, dash.Delay + 200, true, true))
                                )
                            {
                                /* Dont dash always to the edge*/
                                Candidates.Add(Candidate);
                            }
                        }
                    }
                }

                if (Candidates.Count > 0)
                {
                    Vector2 DashPos = Utils.GetClosestVector(from, Candidates);
                    if (dash.Name == "Flash")
                    {
                        ObjectManager.Player.SummonerSpellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                    }
                    else
                    {
                        ObjectManager.Player.Spellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                    }
                    return true;
                }

                /* Closest Point*/
                foreach (var Polygon in Polygons)
                {
                    for (int i = 0; i < Polygon.Count; i++)
                    {
                        Vector2 A = Polygon[i];
                        Vector2 B = Polygon[(i == Polygon.Count - 1) ? 0 : (i + 1)];

                        Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(A, B,
                            Utils.To2D(ObjectManager.Player.ServerPosition));
                        var Candidate = (Vector2)objects1[0];


                        Vector2 PDirection = Utils.perpendicular(A - B);
                        PDirection.Normalize();

                        /* Maybe offset the polygon in the future */
                        Vector2 test = Candidate - ExtraW * PDirection;

                        if (IsSafe(test))
                        {
                            Candidate = (Candidate - ExtraW * PDirection);
                            //Evade ExtraW units after exiting the polygon to avoid roundings
                        }
                        else
                        {
                            Candidate = (Candidate + ExtraW * PDirection);
                            //Stop ExtraW units after exiting the polygon to avoid roundings.
                        }

                        var newpath = new List<Vector2>();
                        newpath.Add(Utils.To2D(ObjectManager.Player.ServerPosition));
                        newpath.Add(Candidate);
                        if ((Vector2.DistanceSquared(Utils.To2D(ObjectManager.Player.ServerPosition), Candidate) <=
                             dash.MaxRange * dash.MaxRange) &&
                            (dash.IsBlink && IsSafeToBlink(Candidate, dash.Delay) ||
                             !dash.IsBlink && IsSafeEvadePath(newpath, dash.Speed, dash.Delay, true, true))
                            )
                        {
                            /* Dont dash always to the edge*/
                            Candidates.Add(Candidate);
                        }
                    }
                }

                if (Candidates.Count > 0)
                {
                    Vector2 DashPos = Utils.GetClosestVector(from, Candidates);
                    if (dash.Name == "Flash")
                    {
                        ObjectManager.Player.SummonerSpellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                    }
                    else
                    {
                        ObjectManager.Player.Spellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                    }
                    return true;
                }

                return true;
            }

            return false;
        }


        private static Vector2 GetWalkingEvadeLocation()
        {
            List<List<Vector2>> Polygons = ClippedPolygon;
            var Candidates = new List<Vector2>();
            List<Vector2> MyWaypoints = Utils.GetWaypoints(ObjectManager.Player);

            Vector2 from = MyWaypoints[MyWaypoints.Count - 1];

            /* Greedy search*/
            foreach (var Polygon in Polygons)
            {
                for (int i = 0; i < Polygon.Count; i++)
                {
                    Vector2 A = Polygon[i];
                    Vector2 B = Polygon[(i == Polygon.Count - 1) ? 0 : (i + 1)];
                    Vector2 Dir = (B - A);
                    Dir.Normalize();
                    float dist = Vector2.Distance(A, B);

                    
                    int C = Math.Min(Math.Max(3, (int)(dist / SearchStep) + 1), 20);
                    for (int j = -1; j < C; j++)
                    {
                        Vector2 Candidate = A + j * Dir * dist / C;

                        if (j == -1)
                        {
                            Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(A, B, Utils.To2D(ObjectManager.Player.ServerPosition));
                            Candidate = (Vector2)objects1[0];
                        }

                        Vector2 PDirection = Utils.perpendicular(A - B);
                        PDirection.Normalize();

                        /* Maybe offset the polygon in the future */
                        Vector2 test = Candidate - ExtraW * PDirection;

                        if (IsSafe(test))
                        {
                            Candidate = (Candidate - ExtraW * PDirection);
                            //Evade ExtraW units after exiting the polygon to avoid roundings
                        }
                        else
                        {
                            Candidate = (Candidate + ExtraW * PDirection);
                            //Stop ExtraW units after exiting the polygon to avoid roundings.
                        }


                        if (IsSafeEvadePath(Utils.GetMyPath(Candidate), ObjectManager.Player.MoveSpeed, Game.Ping/2 + SmoothEvadeBufferT,
                            true, false))
                        {
                            Candidates.Add(Candidate);
                        }
                    }
                }
            }


            if (Candidates.Count > 0)
                return Utils.GetClosestVector(from, Candidates);

            /* Closest Point*/
            foreach (var Polygon in Polygons)
            {
                for (int i = 0; i < Polygon.Count; i++)
                {
                    Vector2 A = Polygon[i];
                    Vector2 B = Polygon[(i == Polygon.Count - 1) ? 0 : (i + 1)];

                    Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(A, B,
                        Utils.To2D(ObjectManager.Player.ServerPosition));
                    var Candidate = (Vector2)objects1[0];

                    Vector2 PDirection = Utils.perpendicular(A - B);
                    PDirection.Normalize();

                    /* Maybe offset the polygon in the future */
                    Vector2 test = Candidate - ExtraW * PDirection;

                    if (IsSafe(test))
                    {
                        Candidate = (Candidate - ExtraW * PDirection);
                        //Evade ExtraW units after exiting the polygon to avoid roundings
                    }
                    else
                    {
                        Candidate = (Candidate + ExtraW * PDirection);
                        //Stop ExtraW units after exiting the polygon to avoid roundings.
                    }

                    if (IsSafeEvadePath(Utils.GetMyPath(Candidate), ObjectManager.Player.MoveSpeed, Game.Ping/2 /*+ ServerTick?*/, true,
                        false))
                    {
                        Candidates.Add(Candidate);
                    }
                }
            }

            return (Candidates.Count > 0) ? Utils.GetClosestVector(from, Candidates) : new Vector2();
        }


        private static bool IsSafeEvadePath(List<Vector2> path, float MySpeed, int ExtraDelay, bool ToEvade, bool ToDash)
        {
            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    Object[] objects1 = entry.Value.IsSafeToTake(path, MySpeed, ExtraDelay, ToEvade, ToDash);
                    Object[] objects2 = entry.Value.IsSafeToTake(path, MySpeed, -10, ToEvade, ToDash);

                    var SafeToTake = (bool)objects1[0];
                    var NeedToEvadeThis = (bool)objects1[2];
                    var Intersection = (Vector2)objects1[1];

                    var SafeToTake2 = (bool)objects2[0];
                    var NeedToEvadeThis2 = (bool)objects2[2];
                    var Intersection2 = (Vector2)objects2[1];

                    if (!SafeToTake2 || !SafeToTake)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsSafeToBlink(Vector2 point, int BlinkDelay)
        {
            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    if (!entry.Value.IsSafeToBlink(point, BlinkDelay))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ProcessPath(List<Vector2> path, bool fromSendPacket)
        {
            var Intersections = new List<Vector2>();
            bool NeedToEvade = false;
            bool NeedToCutThePath = false;

            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    Object[] objects1 = entry.Value.IsSafeToTake(path, ObjectManager.Player.MoveSpeed,
                        fromSendPacket ? Game.Ping / 2 : 0, false, false);

                    var SafeToTake = (bool)objects1[0];
                    var NeedToEvadeThis = (bool)objects1[2];

                    var Intersection = (Vector2)objects1[1];

                    if (SafeToTake == false) //opz
                    {
                        if (NeedToEvadeThis)
                        {
                            NeedToEvade = true;
                        }
                        else
                        {
                            NeedToCutThePath = true;
                            Intersections.Add(Intersection);
                        }
                    }
                }
            }


            if (NeedToEvade)
            {
                if (!fromSendPacket)
                {
                    //for(int k = 0; k < ActiveSkillshotsCount();k++)
                    {
                        if (ConfigEvadeWalking)
                        {
                            Vector2 v = GetWalkingEvadeLocation();
                            if (Utils.IsValidVector2(v))
                            {
                                Evading = true;
                                EvadePoint = v;
                                LastCTick = Environment.TickCount;
                                return true;
                            }
                        }
                        if (Dashes.ContainsKey(ObjectManager.Player.BaseSkinName) && ConfigUseDashes)
                        {
                            if (UseDashes(Utils.To2D(ObjectManager.Player.ServerPosition)) && EvadeDashing(Dashes[ObjectManager.Player.BaseSkinName]))
                            {
                                return true;
                            }
                        }

                        if (Dashes.ContainsKey("Flash") && ConfigUseFlash)
                        {
                            if (UseFlash(Utils.To2D(ObjectManager.Player.ServerPosition)) && EvadeDashing(Dashes["Flash"]))
                            {
                                return true;
                            }
                        }

                        CantEvade = true;
                        return true;
                        //Replace this disabling the less important skillshots since its imposible to evade, dont forget to rebuild the clipped polygon.
                    }
                }
            }

            if (NeedToCutThePath)
            {
                //Game.PrintChat("Sending move packet");
                List<Vector2> SortedList =
                    Intersections.OrderBy(o => Utils.DistanceToPointInPath(path, o, true)).ToList();
                if (fromSendPacket)
                {
                    List<Vector2> MyWaypoints = Utils.GetWaypoints(ObjectManager.Player);
                    if (IsSafe(Utils.To2D(ObjectManager.Player.ServerPosition)) &&
                        Vector2.Distance(MyWaypoints[0], SortedList[0]) < MinMove)
                    {
                        //Avoid looking like a cheater:D
                    }
                    else if (!CantEvade && !Evading)
                        Utils.SendMoveToPacket(SortedList[0]);
                }
                else if (!CantEvade && !Evading)
                    Utils.SendMoveToPacket(SortedList[0]);

                return true;
            }

            return false;
        }

        private static void OnSendPacket(GamePacketSendEventArgs args)
        {
            if (!ConfigEvadingEnabled)
                return;
            
            /* TODO: Block S_CAST packets while evading. */
            if (args.PacketId == 113) //S_MOVE
            {
                if (CantEvade) return;

                var stream = new MemoryStream(args.PacketData);
                var b = new BinaryReader(stream);
                b.BaseStream.Position = b.BaseStream.Position + 5;
                byte[] MoveType = b.ReadBytes(1);

                float X = BitConverter.ToSingle(b.ReadBytes(4), 0);
                float Y = BitConverter.ToSingle(b.ReadBytes(4), 0);
                int targetNid = BitConverter.ToInt32(b.ReadBytes(4), 0);

                if (MoveType[0] == 3 && !Evading) //Auto-attack
                {
                    var target = ObjectManager.GetUnitByNetworkId<Obj_AI_Base>(targetNid);
                    if (target != null)
                    {
                        if (
                            Vector2.Distance(Utils.To2D(target.ServerPosition),
                                Utils.To2D(ObjectManager.Player.ServerPosition)) <
                            (ObjectManager.Player.AttackRange + ObjectManager.Player.BoundingRadius +
                             target.BoundingRadius))
                        {
                            return; //Autoattack stff inside the danger polygon.
                        }
                    }
                }

                if (Evading && MoveType[0] == 2)
                {
                    if (IsSafeEvadePath(Utils.GetMyPath(new Vector2(X, Y)), ObjectManager.Player.MoveSpeed,
                        Game.Ping / 2 + SmoothEvadeBufferT,
                        true, false) && IsSafeEvadePath(Utils.GetMyPath(new Vector2(X, Y)), ObjectManager.Player.MoveSpeed,
                        Game.Ping / 2,
                        true, false) && Environment.TickCount - LastCTick > 100 && IsSafe(new Vector2(X, Y)))
                    {
                        EvadePoint = new Vector2(X, Y);
                        LastCTick = Environment.TickCount;
                    }
                }
                
                if (Evading)
                {
                    args.Process = false;
                    return;
                }

                if (ProcessPath(Utils.GetMyPath(new Vector2(X, Y)), true)) //Actually this same path is on the packet but better calculate it again hehe.
                {
                    args.Process = false;

                    //Game.PrintChat("You shall not pass!");
                }


            }
        }

        /* Returns the skillshots that will hit us*/
        private static List<SkillShot> GetActiveSkillshots(Vector2 point)
        {
            var Result = new List<SkillShot>();
            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    if (entry.Value.IsSafe(point) == false)
                    {
                        Result.Add(entry.Value);
                    }
                }
            }
            return Result;
        }

        private static bool UseDashes(Vector2 point)
        {
            var SList = GetActiveSkillshots(point);
            foreach (var sshot in SList)
            {
                if (sshot.UseDashes)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool UseFlash(Vector2 point)
        {
            var SList = GetActiveSkillshots(point);
            foreach (var sshot in SList)
            {
                if (sshot.UseFlash)
                {
                    return true;
                }
            }
            return false;
        }

        /* Returns if a point is outside the danger area */
        private static bool IsSafe(Vector2 point)
        {
            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    if (entry.Value.IsSafe(point) == false)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /* Returns the intersection points between a path and polygons. */

        private static List<Vector2> GetPolygonIntersections(List<Vector2> path, List<List<Vector2>> Polygons, int Count)
        {
            var Result = new List<Vector2>();

            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 A = path[i];
                Vector2 B = path[i + 1];
                Vector2 Direction = (B - A);
                Direction.Normalize();

                var FoundIntersections = new List<Vector2>();

                foreach (var Polygon in Polygons)
                {
                    for (int j = 0; j < Polygon.Count; j++)
                    {
                        Vector2 C = Polygon[j];
                        Vector2 D = Polygon[(j == Polygon.Count - 1) ? 0 : (j + 1)];

                        Vector2 Intersection = Utils.LineSegmentIntersection(A, B, C, D);
                        if (Utils.IsValidVector2(Intersection))
                        {
                            Vector2 PDirection = Utils.perpendicular(D - C);
                            PDirection.Normalize();

                            Vector2 test = Intersection - ExtraW * PDirection;
                            /* maybe offset the polygon in the future. */
                            if (IsSafe(test))
                            {
                                FoundIntersections.Add(Intersection - ExtraW * PDirection);
                                //Stop 5 units before entering the polygon to avoid roundings.
                            }
                            else
                            {
                                FoundIntersections.Add(Intersection + ExtraW * PDirection);
                                //Stop 5 units before entering the polygon to avoid roundings.
                            }
                        }
                    }
                }

                if (FoundIntersections.Count > 0)
                {
                    //Sort this path Intersections and insert them to Result
                    List<Vector2> SortedList = FoundIntersections.OrderBy(o => Vector2.Distance(o, A)).ToList();
                    Result.AddRange(SortedList);
                }

                if (Result.Count >= Count)
                {
                    return Result; //Returns the Path and polygon intersections sorted by the order they happen.
                }
            }

            return Result;
        }

        /* Returns the number of active skillshots */

        private static int ActiveSkillshotsCount()
        {
            int Result = 0;
            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    Result++;
                }
            }
            return Result;
        }

        /* Clips the polygons of all skillshots */

        private static List<List<Vector2>> GetClippedPolygons()
        {
            int ActiveSkillshotsCount = 0;
            var Result = new List<List<Vector2>>();

            foreach (var entry in SkillShots)
            {
                if (entry.Value.IsActive(0))
                {
                    ActiveSkillshotsCount++;
                }
            }

            if (ActiveSkillshotsCount > 0)
            {
                var subj = new Paths(ActiveSkillshotsCount);

                var clip = new Paths(ActiveSkillshotsCount);

                foreach (var entry in SkillShots)
                {
                    if (entry.Value.IsActive(0))
                    {
                        clip.Add(entry.Value.Polygon);
                        subj.Add(entry.Value.Polygon);
                    }
                }

                var solution = new Paths();

                var c = new Clipper();

                c.AddPaths(subj, PolyType.ptSubject, true);
                c.AddPaths(clip, PolyType.ptClip, true);
                c.Execute(ClipType.ctUnion, solution,
                    PolyFillType.pftPositive, PolyFillType.pftEvenOdd);


                //Game.PrintChat(solution[0].Count.ToString());

                return Utils.ClipperPathsToPolygons(solution);
            }
            return Result;
        }

        private static void OnDraw(EventArgs args)
        {
            if (!ConfigDrawingEnabled) return;

            /* Draw the clipped polygon */
            foreach (var Polygon in ClippedPolygon)
            {
                for (int i = 0; i < Polygon.Count; i++)
                {
                    float[] p1 = Drawing.WorldToScreen(Utils.To3D(Polygon[i]));
                    float[] p2 = Drawing.WorldToScreen(Utils.To3D(Polygon[(i == Polygon.Count - 1) ? 0 : (i + 1)]));

                    Drawing.DrawLine(p1[0], p1[1], p2[0], p2[1], 2, Color.Black);
                }
            }

            foreach (Vector2 circ in DrawCircles)
            {
                Drawing.DrawCircle(Utils.To3D(circ), 100, Color.Red);
            }

            /*foreach (Vector2 circ in Utils.GetMyPath(Utils.To2D(Game.CursorPos)))
            {
                Drawing.DrawCircle(Utils.To3D(circ), 100, Color.Yellow);
            }*/
        }

        public class Dash
        {
            public bool DASH_HERO_ALLY;
            public bool DASH_HERO_ENEMY;
            public bool DASH_MINION_ALLY;
            public bool DASH_MINION_ENEMY;
            public bool DASH_OBJECT_WARD;
            public bool DASH_SKILLSHOT;
            public int Delay;
            public bool FixedRange;
            public bool IsBlink;
            public float MaxRange;
            public SpellSlot Slot;
            public float Speed;
            public string Name;
            public Dash(int delay, float speed, float maxrange, bool fixedrange, SpellSlot slot, bool isblink,
                bool DASH_SKILLSHOT,
                bool DASH_HERO_ENEMY,
                bool DASH_HERO_ALLY,
                bool DASH_OBJECT_WARD,
                bool DASH_MINION_ALLY,
                bool DASH_MINION_ENEMY)
            {
                /* Set up the dash data */
                Delay = delay;
                Speed = speed;
                MaxRange = maxrange;
                FixedRange = fixedrange;
                Slot = slot;
                IsBlink = isblink;
                this.DASH_SKILLSHOT = DASH_SKILLSHOT;

                this.DASH_HERO_ENEMY = DASH_HERO_ENEMY;
                this.DASH_HERO_ALLY = DASH_HERO_ALLY;

                this.DASH_OBJECT_WARD = DASH_OBJECT_WARD;

                this.DASH_MINION_ALLY = DASH_MINION_ALLY;
                this.DASH_MINION_ENEMY = DASH_MINION_ENEMY;
            }

            public List<Obj_AI_Base> GetPosibleEvadeTargets()
            {
                var Result = new List<Obj_AI_Base>();

                if (DASH_MINION_ALLY || DASH_MINION_ENEMY)
                    foreach (Obj_AI_Minion obj in ObjectManager.Get<Obj_AI_Minion>())
                    {
                        //Check if its valid
                        if (
                            Vector2.Distance(Utils.To2D(obj.ServerPosition),
                                Utils.To2D(ObjectManager.Player.ServerPosition)) <= MaxRange)
                        {
                            if (obj.IsAlly && DASH_MINION_ALLY || obj.IsEnemy && DASH_MINION_ENEMY)
                            {
                                Result.Add(obj);
                            }
                        }
                    }

                if (DASH_HERO_ALLY || DASH_HERO_ENEMY)
                    foreach (Obj_AI_Hero obj in ObjectManager.Get<Obj_AI_Hero>())
                    {
                        //Check if its valid
                        if (
                            Vector2.Distance(Utils.To2D(obj.ServerPosition),
                                Utils.To2D(ObjectManager.Player.ServerPosition)) <= MaxRange)
                        {
                            if (obj.IsAlly && DASH_HERO_ALLY || obj.IsEnemy && DASH_HERO_ENEMY)
                            {
                                Result.Add(obj);
                            }
                        }
                    }

                if (DASH_OBJECT_WARD)
                    foreach (Obj_AI_Base obj in ObjectManager.Get<Obj_AI_Base>())
                    {
                        if (
                            Vector2.Distance(Utils.To2D(obj.Position), Utils.To2D(ObjectManager.Player.ServerPosition)) <=
                            MaxRange)
                        {
                            if (obj.IsAlly)
                            {
                                Result.Add(obj);
                            }
                        }
                    }

                return Result;
            }
        }


        public class SkillShot
        {
            public float Angle;
            public Obj_AI_Base Caster;
            public bool Dangerous;

            public int Delay;
            public string name;

            public Vector2 Direction;
            public bool DontCross;
            public Vector2 EndPos;
            public bool Evade;
            public int ExtraDuration;
            public bool FixedRange;
            public int MissileSpeed;
            public Vector2 PBL;
            public Vector2 PBR;
            public Vector2 PTL;
            public Vector2 PTR;
            public Vector2 Perpendicular;

            public Path Polygon;
            public int Range;
            public int RingWidth;
            public Vector2 StartPos;
            public int StartT;
            public bool UseDashes;
            public bool UseFlash;
            public int Width;
            public bool collision;
            public SkillShotType type;
            public bool LineMissileTrackUnits;
            public string FromObject;
            /* Misc */

            public SkillShot(SkillShotType type, int Delay, int Range, int MissileSpeed, int Width, float Angle,
                bool FixedRange, bool collision, bool Dangerous, bool UseDashes, bool UseFlash)
            {
                this.type = type;
                this.Delay = Delay;
                this.Range = Range;
                this.MissileSpeed = MissileSpeed;
                this.Width = Width + buffer;
                this.Angle = Angle;
                this.FixedRange = FixedRange;

                /* Only for the spells with an extra duration (Lux E for example)*/
                ExtraDuration = 0;

                /* only for ring skillshots*/
                RingWidth = 160;
                this.collision = collision;

                this.Dangerous = Dangerous;

                this.Evade = true;
                this.UseDashes = UseDashes;
                this.UseFlash = UseFlash;
                this.LineMissileTrackUnits = false;
                this.DontCross = false;
                this.FromObject = "";
            }

            public bool IsActive(int t)
            {
                if (!Evade)
                    return false;

                if (ConfigDodgeOnlyDangerous && !this.Dangerous)
                    return false;

                if (StartT + t + Delay + ExtraDuration + 1000 * Vector2.Distance(EndPos, StartPos) / MissileSpeed >=
                    Environment.TickCount)
                    return true;

                return false;
            }


            /* Gets triggered when the skillshot is added. */

            public void OnBeingAdded()
            {
                Direction = (EndPos - StartPos);
                Direction.Normalize();


                /* Handle the angle for linear skillshots: */
                if (Angle != 0 && type == SkillShotType.SKILLSHOT_LINE)
                {
                    /* Rotate the direction vector*/
                    Direction = Utils.Vector2Rotate(Direction, Angle);
                    EndPos = StartPos + Vector2.Distance(EndPos, StartPos) * Direction;
                }

                Perpendicular = Utils.perpendicular(Direction);

                if (type == SkillShotType.SKILLSHOT_NONE)
                    this.StartT = 0;

                /* Make the clipper polygons for each type of skillshot */
                if (type == SkillShotType.SKILLSHOT_LINE)
                {
                    PBL = StartPos + Width * Perpendicular;
                    PBR = StartPos - Width * Perpendicular;
                    PTL = EndPos + Width * Perpendicular;
                    PTR = EndPos - Width * Perpendicular;

                    var Rec = new Path(4);
                    //Startpos
                    Rec.Add(new IntPoint(PBL.X, PBL.Y));
                    Rec.Add(new IntPoint(PBR.X, PBR.Y));

                    //EndPos
                    Rec.Add(new IntPoint(PTR.X, PTR.Y));
                    Rec.Add(new IntPoint(PTL.X, PTL.Y));

                    Polygon = Rec;
                }

                else if (type == SkillShotType.SKILLSHOT_CIRCLE)
                {
                    var Circle = new Path(Sides);
                    /* We want the circle to be inscribed inside the polygon */
                    float CRadius = Width / (float)Math.Cos(2 * Math.PI / Sides);

                    for (int i = 0; i < Sides; i++)
                    {
                        float X = EndPos.X + CRadius * (float)Math.Cos(2 * Math.PI / Sides * i);
                        float Y = EndPos.Y + CRadius * (float)Math.Sin(2 * Math.PI / Sides * i);
                        Circle.Add(new IntPoint(X, Y));
                    }
                    Polygon = Circle;
                }
                else if (type == SkillShotType.SKILLSHOT_CONE)
                {
                    Vector2 Direction2 = Utils.Vector2Rotate(Direction, -Angle / 2);

                    /* We want the circle to be inscribed inside the polygon */
                    int mySides = Math.Max(Sides / 4, (int)(Sides * (Angle / (2 * (float)Math.PI))));

                    float CRadius = Width / (float)Math.Cos(2 * Math.PI / Sides);

                    var Cone = new Path(mySides + 1);
                    Cone.Add(new IntPoint(StartPos.X, StartPos.Y)); /* Start Position*/

                    for (int i = 0; i <= mySides; i++)
                    {
                        Vector2 Direction1 = Utils.Vector2Rotate(Direction2, i * Angle / mySides);
                        Direction1.Normalize();
                        Cone.Add(new IntPoint(StartPos.X + CRadius * Direction1.X, StartPos.Y + CRadius * Direction1.Y));
                    }
                    Polygon = Cone;
                }
                else if (type == SkillShotType.SKILLSHOT_RING)
                {
                    var Ring = new Path(Sides * 2);

                    float CRadius = (Width + RingWidth / 2) / (float)Math.Cos(2 * Math.PI / Sides);
                    float IRadius = (Width - RingWidth / 2);

                    for (int i = 0; i <= Sides; i++)
                    {
                        float X = EndPos.X + CRadius * (float)Math.Cos(2 * Math.PI / Sides * i);
                        float Y = EndPos.Y + CRadius * (float)Math.Sin(2 * Math.PI / Sides * i);
                        Ring.Add(new IntPoint(X, Y));
                    }

                    for (int i = 0; i <= Sides; i++)
                    {
                        float X = EndPos.X + IRadius * (float)Math.Cos(2 * Math.PI / Sides * i);
                        float Y = EndPos.Y - IRadius * (float)Math.Sin(2 * Math.PI / Sides * i);
                        Ring.Add(new IntPoint(X, Y));
                    }

                    Polygon = Ring;
                }
                else if (type == SkillShotType.SKILLSHOT_TRIANGLE)
                {
                    var Triangle = new Path(3);
                    //Startpos
                    PBL = StartPos + Width * Perpendicular;
                    PBR = StartPos - Width * Perpendicular;

                    Triangle.Add(new IntPoint(PBL.X, PBL.Y));
                    Triangle.Add(new IntPoint(PBR.X, PBR.Y));

                    //EndPos
                    Triangle.Add(new IntPoint(EndPos.X, EndPos.Y));

                    Polygon = Triangle;
                }
            }

            
            public void OnCollide(Vector3 position)
            {
                /*Velkoz Q split*/
                if (this.name == "VelkozQ" )
                {
                    AddSkillShot("VelkozQ21", this.Caster, Utils.To2D(position), Utils.To2D(position) + this.Perpendicular, Environment.TickCount, true);
                    AddSkillShot("VelkozQ22", this.Caster, Utils.To2D(position), Utils.To2D(position) - this.Perpendicular, Environment.TickCount, true);
                }
            }

            /* Returns if a point is completely safe (Outside of the skillshot polygon) */

            public bool IsSafe(Vector2 point)
            {
                var p = new IntPoint(point.X, point.Y);
                return Clipper.PointInPolygon(p, Polygon) != 1;
                //Returns 0 if false, -1 if pt is on poly and +1 if pt is in poly. 
            }

            /* Gets called every time the game updates*/

            public void OnTick()
            {
                if (type == SkillShotType.SKILLSHOT_LINE && MissileSpeed != int.MaxValue)
                {
                    //TODO: Edit the polygon when the skillshot is global to let the user stay inside.

                    /* Update the polygon for lineal skillshots that have missile. */
                    Vector2 MissilePos = GetProjectilePos(0);

                    PBL = MissilePos + Width * Perpendicular;
                    PBR = MissilePos - Width * Perpendicular;

                    Polygon[0] = (new IntPoint(PBL.X, PBL.Y));
                    Polygon[1] = (new IntPoint(PBR.X, PBR.Y));
                }

                /*Ahri's Q Sivir Q*/
                if (LineMissileTrackUnits)
                {
                    EndPos = Utils.To2D(Caster.ServerPosition);
                    Direction = EndPos - StartPos;
                    Direction.Normalize();
                    Perpendicular = Utils.perpendicular(Direction);
                    PTL = EndPos + Width * Perpendicular;
                    PTR = EndPos - Width * Perpendicular;
                    Polygon[2] = (new IntPoint(PTR.X, PTR.Y));
                    Polygon[3] = (new IntPoint(PTL.X, PTL.Y));
                }
            }

            /* Returns if you will get hit when blinking to a point */
            /* TODO: Add the option to blink into a skillshot if you can evade walking after that */

            public bool IsSafeToBlink(Vector2 point, int t)
            {

                /* As always custom case to handle the lineal skillshots that have missile */
                if (type == SkillShotType.SKILLSHOT_LINE && MissileSpeed != int.MaxValue && t != 0)
                {
                    /* Check if we will get hit before even dashing */
                    Vector2 ProjPosBeforeDash = GetProjectilePos(0);
                    Vector2 ProjPosAfterDash = GetProjectilePos(t);
                    Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(ProjPosBeforeDash, ProjPosAfterDash,
                        Utils.To2D(ObjectManager.Player.ServerPosition));
                    var WillGetHit = (bool)objects1[2];
                    if (WillGetHit)
                    {
                        return false;
                    }
                }

                if (IsSafe(point))
                {
                    return true;
                }

                /* TODO: Add special case when you can jump just where the missile is atm*/
                if (IsActive(t))
                {
                    return false;
                }

                return true;
            }

            /* Returns if a spell is about to hit a unit, this is usefull for shielding allies for example */

            public bool IsAboutToHit(Obj_AI_Hero unit, int delay)
            {
                Vector2 PredictedUnitPosition = Utils.CutPath(Utils.GetWaypoints(unit), unit.MoveSpeed * delay / 1000);
                /* Special case to linear skillshots that have missile */
                if (type == SkillShotType.SKILLSHOT_LINE && MissileSpeed != int.MaxValue)
                {
                    //TODO
                }
                else
                {
                    if (!IsActive(delay) && IsActive(0)) /* Make the check just when the skillshot is about to expire */
                    {
                        if (!IsSafe(PredictedUnitPosition))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            //Returns if you will hit by this skillshot when taking the path "path" at speed "Speed".
            public Object[] IsSafeToTake(List<Vector2> path, float MySpeed, int ExtraDelay, bool ToEvade, bool ToDash)
            {
                Vector2 StartPoint = path[0];
                var StopPoint = new Vector2();

                bool ImSafe = IsSafe(StartPoint);

                var Polygons = new List<List<Vector2>>();
                Polygons.Add(Utils.ClipperPathToPolygon(Polygon));

                List<Vector2> Intersections = GetPolygonIntersections(path, Polygons, 10);

                if (Intersections.Count == 0) //No intersections 
                {
                    return new object[3] { ImSafe, StopPoint, !ImSafe };
                }

                if (type == SkillShotType.SKILLSHOT_LINE && MissileSpeed != int.MaxValue)
                //This case requires custom logic, rewrite it in the future.
                {
                    if (ImSafe) //We are outside the polygon.
                    {
                        if (Intersections.Count % 2 == 0) //The endPoint of the path is outside the polygon.
                        {
                            //This logic works better if the path inside the skillshot is straight, but since MissileSpeed >>> MovementSpeed this is valid almost always.
                            for (int k = 0; k < (Intersections.Count / 2); k++)
                            {
                                Vector2 EnterPoint = Intersections[k];
                                //This is the point where we enter into the skillshot
                                Vector2 ExitPoint = Intersections[k + 1];
                                //This is the point from where we leave the skillshot.  

                                Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(StartPos, EndPos,
                                    EnterPoint);
                                var EnterPointOnSegment = (Vector2)objects1[0];

                                Object[] objects2 = Utils.VectorPointProjectionOnLineSegment(StartPos, EndPos, ExitPoint);
                                var ExitPointOnSegment = (Vector2)objects2[0];

                                //TODO: Add ping and server tick
                                int TimePassedUntilEnterPoint =
                                    ExtraDelay +
                                    (int)(1000 * Utils.DistanceToPointInPath(path, EnterPoint, false) / MySpeed);
                                int TimePassedUntilExitPoint =
                                    ExtraDelay + (int)(1000 * Utils.DistanceToPointInPath(path, ExitPoint, true) / MySpeed);

                                Vector2 MissilePosWhenEnter = GetProjectilePos(TimePassedUntilEnterPoint);
                                Vector2 MissilePosWhenExit = GetProjectilePos(TimePassedUntilExitPoint);

                                if ((Vector2.Distance(EndPos, MissilePosWhenEnter) + MissileBufferEnd) <
                                    Vector2.Distance(EndPos, EnterPointOnSegment))
                                // The Missile has passed when we enter the skillshot.
                                {
                                    //Game.PrintChat("The skillshot wont be in my way :D");
                                }
                                else //Case 2: The Missile has not passed when we enter the skillshot.
                                {
                                    if ((Vector2.Distance(EndPos, MissilePosWhenExit) -
                                         (ToEvade ? EvadeMissileFrontBuffer : MissileBufferFront)) <
                                        Vector2.Distance(EndPos, ExitPointOnSegment))
                                    // The Missile has passed when we exit the skillshot.
                                    {
                                        //We will get hit
                                        return new object[3] { false, Intersections[k], false };
                                    }
                                }
                            }

                            //Game.PrintChat("The skillshot wont be in my way2 :D");
                            //If we are here it means that the skillshot won't hit us :D
                            return new object[3] { true, Intersections[0], false };
                        }
                        //The endpoint is inside the skillshot, todo: if the intersections > 1 then let the user walk to the first safe waypoint
                        return new object[3] { false, Intersections[0], false };
                    }
                    //We are inside the skillshot
                    if (Intersections.Count % 2 == 1) //The endPoint of the path is outside the polygon.
                    {
                        if (Intersections.Count == 1)
                        {
                            //Check if we can leave in time following the path
                            Vector2 LeavePoint = Intersections[0];
                            int TimePassedUntilLeavePoint =
                                (int)
                                    (1000 *
                                     Utils.DistanceToPointInPath(path, LeavePoint, true) /
                                     MySpeed) + ExtraDelay;
                            //Game.PrintChat(TimePassedUntilLeavePoint.ToString());
                            Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(StartPos, EndPos, LeavePoint);

                            var LeavePointOnSegment = (Vector2)objects1[0];

                            Vector2 MissilePosWhenLeave = GetProjectilePos(TimePassedUntilLeavePoint);

                            if (Vector2.Distance(EndPos, MissilePosWhenLeave) + (ToDash ? DashEvadeBuffer : 0) -
                                (ToEvade ? EvadeMissileFrontBuffer : MissileBufferFront) <=
                                Vector2.Distance(EndPos, LeavePointOnSegment)) //We will get hit
                            {
                                return new object[3] { false, Intersections[0], true };
                            }
                            //Game.PrintChat("Can cross Inside");
                            return new object[3] { true, StopPoint, false };
                        }
                        //Same code as above :p
                        //This logic works better if the path inside the skillshot is straight, but since MissileSpeed >>> MovementSpeed this is valid almost always.

                        for (int k = 1; k < (Intersections.Count + 1) / 2; k++)
                        {
                            Vector2 EnterPoint = Intersections[k]; //This is the point where we enter the skillshot
                            Vector2 ExitPoint = Intersections[k + 1];
                            //This is the point from where we leave the skillshot.  

                            Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(StartPos, EndPos, EnterPoint);
                            var EnterPointOnSegment = (Vector2)objects1[0];

                            Object[] objects2 = Utils.VectorPointProjectionOnLineSegment(StartPos, EndPos, ExitPoint);
                            var ExitPointOnSegment = (Vector2)objects2[0];

                            int TimePassedUntilEnterPoint =
                                (int)
                                    (1000 *
                                     Utils.DistanceToPointInPath(Utils.GetWaypoints(ObjectManager.Player), EnterPoint,
                                         false) /
                                     MySpeed) + ExtraDelay;
                            int TimePassedUntilExitPoint =
                                (int)
                                    (1000 *
                                     Utils.DistanceToPointInPath(Utils.GetWaypoints(ObjectManager.Player), ExitPoint,
                                         true) /
                                     MySpeed) + ExtraDelay;

                            Vector2 MissilePosWhenEnter = GetProjectilePos(TimePassedUntilEnterPoint);
                            Vector2 MissilePosWhenExit = GetProjectilePos(TimePassedUntilExitPoint);

                            if (Vector2.Distance(EndPos, MissilePosWhenEnter) <
                                Vector2.Distance(EndPos, EnterPointOnSegment))
                            // The Missile has passed when we enter the skillshot.
                            {
                                //Game.PrintChat("The skillshot wont be in my way :D");
                            }
                            else //Case 2: The Missile has not passed when we enter the skillshot.
                            {
                                if (Vector2.Distance(EndPos, MissilePosWhenExit) <
                                    Vector2.Distance(EndPos, ExitPointOnSegment))
                                // The Missile has passed when we exit the skillshot.
                                {
                                    //We will get hit
                                    return new object[3] { false, Intersections[k + 1], true };
                                }
                            }
                        }
                        //If we are here it means that we can take the path :D
                        return new object[3] { true, StopPoint, false };
                    }
                    return new object[3] { true, StopPoint, true };
                }


                /* For non-linear skillshots */
                /* TODO: Add this.DontCross */

                int TimeToExplode = StartT + ExtraDelay + Delay +
                                    (int)(1000 * Vector2.Distance(EndPos, StartPos) / MissileSpeed) - Environment.TickCount;

                if (ExtraDuration != 0)
                {
                    if (TimeToExplode <= 0)
                    {
                        if (ImSafe)
                        {
                            return new object[3] { false, Intersections[0], false };
                        }
                        return new object[3] { true, Intersections[0], true };
                    }
                }

                Vector2 MyPosition = Utils.CutPath(path, Math.Max(0, TimeToExplode) * MySpeed / 1000);

                if (IsSafe(MyPosition))
                {
                    return new object[3] { true, StopPoint, false };
                }

                if (ImSafe)
                {
                    return new object[3] { false, Intersections[0], false };
                }

                return new object[3] { false, Intersections[0], true };
            }

            /* Returns where the projectile is going to be after t miliseconds*/

            public Vector2 GetProjectilePos(int t)
            {
                int TimeInTheAir = Math.Max(Environment.TickCount - StartT + t - Delay, 0);

                if (TimeInTheAir > 0)
                {
                    return StartPos +
                           Direction *
                           Math.Max(0, Math.Min(TimeInTheAir * MissileSpeed / 1000, Vector2.Distance(StartPos, EndPos)));
                }

                return StartPos;
            }
        }
    }
}

