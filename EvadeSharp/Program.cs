using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using ClipperLib;
using LeagueSharp;
using SharpDX;
using Color = System.Drawing.Color;
using Path = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;

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

            /* Not added yet */
            SKILLSHOT_TRIANGLE,
            SKILLSHOT_ARC,
        }

        private const int buffer = 4; /* Buffer added to all the skillshots width */
        private const int ExtraW = 10; /* */
        private const int MinMove = 50;
        private const int Sides = 30; /* Number of sides for the circular polygons, the more the better */
        private const int MissileBufferEnd = 0; /* Extra distance at the End of the missile*/
        private const int MissileBufferFront = 100; /* Extra distance in Front of the missile*/
        private const int EvadeMissileFrontBuffer = 10;
        private const bool TestOnAllies = false;
        private const bool ShowSkillShotData = false;
        private const int SearchStep = 50;
        private const int DashEvadeBuffer = 100;
        private const int SmoothEvadeBufferT = 200;
        private static readonly List<Vector2> DrawCircles = new List<Vector2>();
        private static readonly List<Vector3> DrawCircles2 = new List<Vector3>();

        /*Constants*/
        private static int hitbox = 65; /* Actually is not a constant*/
        /***********/

        private static readonly Dictionary<String, String> MissileNameToSpellName = new Dictionary<String, String>();
        private static readonly Dictionary<String, SkillShot> SkillShots = new Dictionary<String, SkillShot>();
        private static readonly Dictionary<String, Dash> Dashes = new Dictionary<String, Dash>();

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

            Game.PrintChat("<font color=\"#\">Evade# Loaded");

            /* Supported SkillShot list, probably this will change in the future */

            SkillShot SK;

            /* Ashe */

            /* W Commented since its a bad idea to evade it always */
            /*SkillShots.Add("Volley", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, 0, true, false));

            SkillShots.Add("Volley1", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, (float)Math.PI / 180 * 9.583f, true, false));
            SkillShots.Add("Volley2", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, (float)Math.PI / 180 * 2 * 9.583f, true, false));
            SkillShots.Add("Volley3", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, (float)Math.PI / 180 * 3 * 9.583f, true, false));

            SkillShots.Add("Volley4", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, -(float)Math.PI / 180 * 9.583f, true, false));
            SkillShots.Add("Volley5", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, -(float)Math.PI / 180 * 2 * 9.583f, true, false));
            SkillShots.Add("Volley6", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 1500, 35 + hitbox, -(float)Math.PI / 180 * 3 * 9.583f, true, false));
            */

            /* R */
            SkillShots.Add("EnchantedCrystalArrow",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 25000, 1600, 130 + hitbox, 0, true, true, true));
            //MissileNameToSpellName.Add("LuxLightBindingMis", "LuxLightBinding");

            /* Annie */

            /*W*/
            SkillShots.Add("Incinerate",
                new SkillShot(SkillShotType.SKILLSHOT_CONE, 600, 1, int.MaxValue, 625, 50f * (float)Math.PI / 180, true, false, false));

            /*R TODO:Detection from fow?*/
            SkillShots.Add("InfernalGuardian",
                           new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 600, int.MaxValue, 251 + hitbox, 0, false, false, true));

            /* Amumu */
            /* Q */
            SkillShots.Add("BandageToss",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 2000, 130 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("SadMummyBandageToss", "BandageToss");

            /* R */
            SkillShots.Add("CurseoftheSadMummy",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 0, int.MaxValue, 550, 0, true, false, true));

            /* Blitzcrank */
            /* Q */
            SkillShots.Add("RocketGrab",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 1800, 70 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("RocketGrab", "RocketGrabMissile");

            /* R */
            SkillShots.Add("StaticField",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 0, int.MaxValue, 600, 0, true, false, true));

            /* Caitlyn */
            /* Q */
            SkillShots.Add("CaitlynPiltoverPeacemaker",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 625, 1300, 2200, 90 + hitbox, 0, true, false, false));
            MissileNameToSpellName.Add("CaitlynPiltoverPeacemaker", "CaitlynPiltoverPeacemaker");

            /* E */
            SkillShots.Add("CaitlynEntrapment",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1000, 2000, 80 + hitbox, 0, true, true, false));
            MissileNameToSpellName.Add("CaitlynEntrapment", "CaitlynEntrapment");

            /*Cassiopeia*/
            /* Q */
            SkillShots.Add("CassiopeiaNoxiousBlast",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 625, 850, int.MaxValue, 130 + hitbox, 0, false, false, false));

            /*R*/
            SkillShots.Add("CassiopeiaPetrifyingGaze",
                new SkillShot(SkillShotType.SKILLSHOT_CONE, 600, 1, int.MaxValue, 825, 80f * (float)Math.PI / 180, true, false, true));

            /*ChoGath*/
            /* Q */
            SkillShots.Add("Rupture",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1200, 950, int.MaxValue, 250 + hitbox, 0, false, false, true));

            /*W*/
            SkillShots.Add("FeralScream",
                new SkillShot(SkillShotType.SKILLSHOT_CONE, 650, 1, int.MaxValue, 650, 2 * 28f * (float)Math.PI / 180, true, false, false));

            /* Elise */
            /* E */
            SkillShots.Add("EliseHumanE",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 1450, 70 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("EliseHumanE", "EliseHumanE");

            /* Ezreal */
            /* Q */
            SkillShots.Add("EzrealMysticShot",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1200, 2000, 60 + hitbox, 0, true, true, false));
            MissileNameToSpellName.Add("EzrealMysticShotMissile", "EzrealMysticShot");

            /* W */
            SkillShots.Add("EzrealEssenceFlux",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1050, 1600, 80 + hitbox, 0, true, false, false));
            MissileNameToSpellName.Add("EzrealEssenceFluxMissile", "EzrealEssenceFlux");

            /* R */
            SkillShots.Add("EzrealTrueshotBarrage",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 1000, 20000, 2000, 160 + hitbox, 0, true, false, true));
            MissileNameToSpellName.Add("EzrealTrueshotBarrage", "EzrealTrueshotBarrage");

            /* Jarvan */
            /* Q */
            SkillShots.Add("JarvanIVDragonStrike",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 1450, 70 + hitbox, 0, true, false, false));

            /* E */
            SkillShots.Add("JarvanIVDemacianStandard",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 500, 860, int.MaxValue, 175 + hitbox, 0, false, false, false));

            /* Jinx */
            /* W TODO: Detect the animation from fow*/
            SkillShots.Add("JinxW",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 600, 1500, 3300, 60 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("JinxWMissile", "JinxW");

            /* R TODO: Take into account the speed change*/
            SkillShots.Add("JinxRWrapper",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 600, 20000, 1700, 140 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("JinxR", "JinxRWrapper");

            /*Karthus*/
            /* Q */
            SkillShots.Add("LayWaste",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 625, 875, int.MaxValue, 160 + hitbox, 0, false, false, false));


            /* Leona */
            /* E */
            SkillShots.Add("LeonaZenithBlade",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 900, 2000, 90 + hitbox, 0, true, false, true));
            MissileNameToSpellName.Add("LeonaZenithBladeMissile", "LeonaZenithBlade");

            /* R TODO: fow detection*/
            SkillShots.Add("LeonaSolarFlare",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1000, 1200, int.MaxValue, 120 + hitbox, 0, false, false, true));

            /* Lux */
            /* Q */
            SkillShots.Add("LuxLightBinding",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1300, 1200, 70 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("LuxLightBindingMis", "LuxLightBinding");

            /* E Not dangerous enough to have it enabled by default, TODO: Delete when the object dissapears*/
            SK = new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1100, 1300, 275 + hitbox, 0, false, false, false);
            SK.ExtraDuration = 5000;
            SkillShots.Add("LuxLightStrikeKugel", SK);
            //MissileNameToSpellName.Add("LuxLightBindingMis", "LuxLightBinding");

            /* R */
            SkillShots.Add("LuxMaliceCannon",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 1350, 3500, int.MaxValue, 190 + hitbox, 0, true, false, true));
            /* TODO: Add detection from fow */

            /* Malphite */
            /* R TODO: fow detection*/
            SkillShots.Add("UFSlash",
                new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 250, 1000, 1500, 270 + hitbox, 0, false, false, true));

            /* Morgana */
            /* Q */
            SkillShots.Add("DarkBindingMissile",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1300, 1200, 70 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("DarkBindingMissile", "DarkBindingMissile");

            /* Nidalee */
            /* Q */
            SkillShots.Add("JavelinToss",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 125, 1500, 1300, 60 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("JavelinToss", "JavelinToss");

            /* Nautilus */
            /* Q */
            SkillShots.Add("NautilusAnchorDrag",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1100, 2000, 90 + hitbox, 0, true, true, true));
            MissileNameToSpellName.Add("NautilusAnchorDragMissile", "NautilusAnchorDrag");

            /* Orianna TODO: Add E and R*/
            /* Q */
            SkillShots.Add("NotRealNameOrianna",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 0, 1500, 1200, 80 + hitbox, 0, false, false, false));//Not real spell name since the delay is 0 and the missile gives us the start position.
            MissileNameToSpellName.Add("orianaizuna", "NotRealNameOrianna");

            /*Riven*/
            /*R*/
            SkillShots.Add("rivenizunablade",
                new SkillShot(SkillShotType.SKILLSHOT_CONE, 500, 1, 2200, 1100, 45f * (float)Math.PI / 180, true, false, true));

            /* Sivir */
            /* Q */
            SkillShots.Add("SivirQ",
                new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1175, 1350, 90 + hitbox, 0, true, false, true));

            SK = new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1175, 1350, 100 + hitbox, 0, true, false, true);
            SK.LineMissileTrackUnits = true;

            SkillShots.Add("SivirQReturn", SK);
            MissileNameToSpellName.Add("SivirQMissileReturn", "SivirQReturn");
            MissileNameToSpellName.Add("SivirQMissile", "SivirQ");

            /* Sona */
            SkillShots.Add("SonaCrescendo",
                    new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1000, 2400, 140 + hitbox, 0, true, false, true));
            MissileNameToSpellName.Add("SonaCrescendo", "SonaCrescendo");

            /*Twisted Fate*/
            SkillShots.Add("WildCards", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1450, 1000, 40 + hitbox, 0, true, false, false));

            SkillShots.Add("WildCards1", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1450, 1000, 40 + hitbox, 28 * (float)Math.PI / 180, true, false, false));
            SkillShots.Add("WildCards2", new SkillShot(SkillShotType.SKILLSHOT_LINE, 250, 1450, 1000, 40 + hitbox, -28 * (float)Math.PI / 180, true, false, false));
            MissileNameToSpellName.Add("SealFateMissile", "SonaCrescendo");
            /* Veigar */
            /*W*/
            SkillShots.Add("VeigarDarkMatter",
        new SkillShot(SkillShotType.SKILLSHOT_CIRCLE, 1350, 900, int.MaxValue, 225 + hitbox, 0, false, false, true));

            /*E*/
            SK = new SkillShot(SkillShotType.SKILLSHOT_RING, 250, 600, int.MaxValue, 350, 0, false, false, true);
            SK.ExtraDuration = 3000;
            SkillShots.Add("VeigarEventHorizon", SK);

            foreach (var skillshot in SkillShots)
                SkillShots[skillshot.Key].name = skillshot.Key;

            /* Dash data */
            //Game.OnWndProc += onwndmsg;
            Dashes.Add("Vayne",
                new Dash(250, 900, 300, true, SpellSlot.Q, false, true, false, false, false, false, false));
            Dashes.Add("Ezreal",
                new Dash(250, float.MaxValue, 475, false, SpellSlot.E, true, true, false, false, false, false, false));
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
            }
        }

        private static void AddSkillShot(string Name, Obj_AI_Base unit, Vector2 StartPos, Vector2 EndPos, int StartT, bool FromProcessSpell)
        {
            if (!unit.IsEnemy && !TestOnAllies)
                return;


            /* Don't add it if it's already added */
            if (SkillShots[Name].IsActive(0))
                return;

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
                SkillShots[Name].Caster = unit; /* Not used yet */
                SkillShots[Name].StartT = StartT;
                SkillShots[Name].StartPos = StartPos;

                if ((Vector2.Distance(Utils.To2D(unit.ServerPosition), EndPos) >
                     SkillShots[Name].Range) || SkillShots[Name].FixedRange)
                {
                    Vector2 Direction = EndPos - StartPos;
                    Direction.Normalize();
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
            if (SkillShots.ContainsKey(Spell.SData.Name))
            {
                if (Spell.SData.Name == "UFSlash")
                    SkillShots["UFSlash"].MissileSpeed = 1500 + (int)unit.MoveSpeed;

                AddSkillShot(Spell.SData.Name, unit, Utils.To2D(unit.ServerPosition), Utils.To2D(Spell.End),
                    Environment.TickCount - Game.Ping / 2, true);
            }

            if (ShowSkillShotData)
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
        }

        /* For skillshot detection when they are from fow*/

        private static void OnCreateMissile(GameObject sender, EventArgs args)
        {
            return;
            if (sender is Obj_SpellMissile)
            {
                var obj = (Obj_SpellMissile)sender;


                if (MissileNameToSpellName.ContainsKey(obj.SData.Name))
                {
                    Game.PrintChat("FOW");

                    Vector3Time[] path = obj.GetPath(0.001f);
                    Vector2 MissilePos = Utils.To2D(obj.Position);
                    Vector2 EndPoint = new Vector2();

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

                if (ShowSkillShotData)
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
                        SkillShots[MissileNameToSpellName[obj.SData.Name]].StartT = 0;
                }
            }
        }

        private static void OnTick(EventArgs args)
        {
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
            if (ObjectManager.Player.Spellbook.CanUseSpell(dash.Slot) != SpellState.Ready)
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
                    ObjectManager.Player.Spellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
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
                        int C = Math.Max(3, (int)(dist / SearchStep));
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
                                (dash.IsBlink && IsSafeToBlink(Candidate, dash.Delay + SmoothEvadeBufferT) ||
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
                    if (Candidates.Count > 0)
                    {
                        Vector2 DashPos = Utils.GetClosestVector(@from, Candidates);
                        ObjectManager.Player.Spellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
                        return true;
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
                    Vector2 DashPos = Utils.GetClosestVector(@from, Candidates);
                    ObjectManager.Player.Spellbook.CastSpell(dash.Slot, Utils.To3D(DashPos));
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
                    int C = Math.Max(3, (int)(dist / 50));
                    for (int j = 0; j < C; j++)
                    {
                        Vector2 Candidate = A + j * Dir * dist / C;

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


                        if (IsSafeEvadePath(Utils.GetMyPath(Candidate), ObjectManager.Player.MoveSpeed, Game.Ping + SmoothEvadeBufferT,
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

                    if (IsSafeEvadePath(Utils.GetMyPath(Candidate), ObjectManager.Player.MoveSpeed, Game.Ping /*+ ServerTick?*/, true,
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
                        /* The skillshot imposible to evade or Remove the less dangerous skillshot  */
                        Vector2 v = GetWalkingEvadeLocation();
                        if (Utils.IsValidVector2(v))
                        {
                            Evading = true;
                            EvadePoint = v;
                            return true;
                        }
                        if (Dashes.ContainsKey(ObjectManager.Player.BaseSkinName))
                        {
                            if (!EvadeDashing(Dashes[ObjectManager.Player.BaseSkinName]))
                            {
                                CantEvade = true;
                            }
                            else
                            {
                                return true;
                            }
                        }
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
                int targetNid = BitConverter.ToInt32(b.ReadBytes(4), 0) - 1;


                if (MoveType[0] == 3 && !Evading) //Auto-attack
                {
                    var target = ObjectManager.GetUnitByNetworkId<Obj_AI_Base>(targetNid);
                    if (target is Obj_AI_Base && target.IsValid)
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

                if (Evading)
                {
                    args.Process = false;
                    //Evading = false;
                    //OnTick(new EventArgs());
                    //Game.PrintChat("I don't want to go there, Im evading :<");
                    return;
                }

                if (ProcessPath(Utils.GetMyPath(new Vector2(X, Y)), true)) //Actually this same path is on the packet but better calculate it again hehe.
                {
                    args.Process = false;
                    //Game.PrintChat("You shall not pass!");
                }
            }
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
                MaxRange = delay;
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
            /* Misc */

            public SkillShot(SkillShotType type, int Delay, int Range, int MissileSpeed, int Width, float Angle,
                bool FixedRange, bool collision, bool Dangerous)
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
                RingWidth = 100;
                this.collision = collision;

                this.Dangerous = Dangerous;

                Evade = true;
                UseDashes = false;
                UseFlash = false;
                LineMissileTrackUnits = false;
                DontCross = false;
            }

            public bool IsActive(int t)
            {
                if (!Evade)
                    return false;

                if (StartT + t + Delay + ExtraDuration + 1000 * Vector2.Distance(EndPos, StartPos) / MissileSpeed >
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
                if (type == SkillShotType.SKILLSHOT_LINE && MissileSpeed != int.MaxValue)
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
