using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Microsoft.FlightSimulator.SimConnect;
using System.Timers;
using System.Runtime.InteropServices;
using System.Xml;
using System.IO;
using System.Threading;
using System.Data.OleDb;
using System.Drawing;
using System.Windows.Forms;

namespace Fsxget
{
    public class FsxConnection
	{

		#region Classes
        public class ObjectData<T>
		{
			private bool bModified;
			private T tValue;

			public ObjectData()
			{
				tValue = default(T);
				bModified = false;
			}

            public T Value
			{
				get
				{
					bModified = false;
					return tValue;
				}
				set
				{
					if ((tValue == null && value != null) || !tValue.Equals(value))
					{
						bModified = true;
						tValue = value;
					}
				}
			}

			public bool IsModified
			{
				get
				{
					return bModified;
				}
			}
		}
		
        public class ObjectPosition
		{
			private ObjectData<float> fLon;
			private ObjectData<float> fLat;
			private ObjectData<float> fAlt;
			private double dTime;

			public ObjectPosition()
			{
				fLon = new ObjectData<float>();
				fLat = new ObjectData<float>();
				fAlt = new ObjectData<float>();
			}

			public ObjectData<float> Longitude
			{
				get
				{
					return fLon;
				}
			}
			public ObjectData<float> Latitude
			{
				get
				{
					return fLat;
				}
			}
			public ObjectData<float> Altitude
			{
				get
				{
					return fAlt;
				}
			}
			public double Time
			{
				get
				{
					return dTime;
				}
				set
				{
					dTime = value;
				}
			}
			public bool HasMoved
			{
				get
				{
					return (Longitude.IsModified || Latitude.IsModified || Altitude.IsModified);
				}
			}
			public String Coordinate
			{
				get
				{
					return XmlConvert.ToString(fLon.Value) + "," + XmlConvert.ToString(fLat.Value) + "," + XmlConvert.ToString(fAlt.Value);
				}
			}
		}
		public class SceneryObject
		{
			public enum STATE
			{
				NEW,
				UNCHANGED,
				MODIFIED,
				DELETED,
				DATAREAD,
			}

			private STATE tState;
			private DATA_REQUESTS tType;
			private uint unID;
			public bool bDataRecieved;

			public SceneryObject(uint unID, DATA_REQUESTS tType)
			{
				tState = STATE.NEW;
				bDataRecieved = true;
				this.tType = tType;
				this.unID = unID;
			}

			public STATE State
			{
				get
				{
					return tState;
				}
				set
				{
					tState = value;
				}
			}
			public uint ObjectID
			{
				get
				{
					return unID;
				}
			}
			public DATA_REQUESTS ObjectType
			{
				get
				{
					return tType;
				}
			}
		}
		public class SceneryMovingObject : SceneryObject
		{
			#region Classes
			public class ObjectPath
			{
                private ObjectPosition lastPos;
                private String strCoordinates;
				STATE tState;

				public ObjectPath()
				{
                    lastPos = new ObjectPosition();
                    strCoordinates = "";
					tState = STATE.NEW;
				}

				public ObjectPath(ref StructBasicMovingSceneryObject obj)
				{
                    lastPos = new ObjectPosition();
                    tState = STATE.NEW;
					AddPosition(ref obj);
				}

				public void AddPosition(ref StructBasicMovingSceneryObject obj)
				{
                    lastPos.Longitude.Value = (float)obj.dLongitude;
                    lastPos.Latitude.Value = (float)obj.dLatitude;
                    lastPos.Altitude.Value = (float)obj.dAltitude;
                    if (lastPos.HasMoved)
                    {
                        strCoordinates += XmlConvert.ToString(lastPos.Longitude.Value) + "," + XmlConvert.ToString(lastPos.Latitude.Value) + "," + XmlConvert.ToString(lastPos.Altitude.Value) + " ";
                        if (tState == STATE.DATAREAD)
                            tState = STATE.MODIFIED;
                    }
				}

				public void Clear()
				{
					strCoordinates = "";
					if (tState == STATE.DATAREAD)
						tState = STATE.MODIFIED;
				}

				public STATE State
				{
					get
					{
						return tState;
					}
					set
					{
						tState = value;
					}
				}

				public String Coordinates
				{
					get
					{
						return strCoordinates;
					}
				}
			}
			public class PathPrediction
			{
				private bool bPredictionPoints;
				public ObjectPosition[] positions;
                private double dTimeElapsed;
				STATE tState;

				public PathPrediction(bool bWithPoints)
				{
					tState = STATE.NEW;
					HasPoints = bWithPoints;
					dTimeElapsed = 0;
				}

				public void Update(ref StructBasicMovingSceneryObject obj)
				{
					dTimeElapsed = obj.dTime - positions[0].Time;
					if (dTimeElapsed > 0)
					{
						for (int i = 1; i < positions.Length; i++)
						{
							CalcPositionByTime(ref obj, ref positions[i]);
						}
						positions[0].Longitude.Value = (float)obj.dLongitude;
						positions[0].Latitude.Value = (float)obj.dLatitude;
						positions[0].Altitude.Value = (float)obj.dAltitude;
						positions[0].Time = obj.dTime;

						if (tState == STATE.DATAREAD)
						{
							if (positions[0].HasMoved)
								tState = STATE.MODIFIED;
							else
								tState = STATE.UNCHANGED;
						}
					}
				}

				private void CalcPositionByTime(ref StructBasicMovingSceneryObject objNew, ref ObjectPosition tResultPos)
				{
					double dScale = tResultPos.Time / dTimeElapsed;

					tResultPos.Latitude.Value = (float)(objNew.dLatitude + dScale * (objNew.dLatitude - positions[0].Latitude.Value));
					tResultPos.Longitude.Value = (float)(objNew.dLongitude + dScale * (objNew.dLongitude - positions[0].Longitude.Value));
					tResultPos.Altitude.Value = (float)(objNew.dAltitude + dScale * (objNew.dAltitude - positions[0].Altitude.Value));
				}

				public bool HasPoints
				{
					get
					{
						return bPredictionPoints;
					}
					set
					{
						if (bPredictionPoints != value)
						{
							bPredictionPoints = value;
							SettingsList lstPoint = (SettingsList)Program.Config[Config.SETTING.PREDICTION_POINTS];
							if (bPredictionPoints)
							{
								positions = new ObjectPosition[lstPoint.listSettings.Count + 1];
								for (int i = 0; i < positions.Length; i++)
								{
									positions[i] = new ObjectPosition();
									if (i > 0)
										positions[i].Time = (double)lstPoint["Time", i - 1].IntValue;
								}
							}
						}
						if (!bPredictionPoints && positions == null)
						{
							positions = new ObjectPosition[2];
							positions[0] = new ObjectPosition();
							positions[1] = new ObjectPosition();
							positions[1].Time = 1200;
						}
						positions[0].Time = 0;
					}
				}
				public ObjectPosition[] Positions
				{
					get
					{
						return positions;
					}
				}

				public STATE State
				{
					get
					{
						return tState;
					}
					set
					{
						tState = value;
					}
				}
			}
			#endregion

			#region Variables
			private ObjectData<String> strTitle;
			private ObjectData<String> strATCType;
			private ObjectData<String> strATCModel;
			private ObjectData<String> strATCID;
			private ObjectData<String> strATCAirline;
			private ObjectData<String> strATCFlightNumber;
			private ObjectPosition objPos;
			private ObjectData<float> fHeading;
			private double dTime;
			public ObjectPath objPath;
			public PathPrediction pathPrediction;
			#endregion

			public SceneryMovingObject(uint unID, DATA_REQUESTS tType, ref StructBasicMovingSceneryObject obj)
				: base(unID, tType)
			{
				strTitle = new ObjectData<String>();
				strATCType = new ObjectData<String>();
				strATCModel = new ObjectData<String>();
				strATCID = new ObjectData<String>();
				strATCAirline = new ObjectData<String>();
				strATCFlightNumber = new ObjectData<String>();
				objPos = new ObjectPosition();
				objPath = new ObjectPath(ref obj);
				fHeading = new ObjectData<float>();
				strTitle.Value = obj.szTitle;
				strATCType.Value = obj.szATCType;
				strATCModel.Value = obj.szATCModel;
				strATCID.Value = obj.szATCID;
				strATCAirline.Value = obj.szATCAirline;
				strATCFlightNumber.Value = obj.szATCFlightNumber;
				objPos.Longitude.Value = (float)obj.dLongitude;
				objPos.Latitude.Value = (float)obj.dLatitude;
				objPos.Altitude.Value = (float)obj.dAltitude;
				fHeading.Value = (float)obj.dHeading;
				dTime = obj.dTime;
				ConfigChanged();
			}

			public void Update(ref StructBasicMovingSceneryObject obj)
			{
				if (State == STATE.DELETED)
					return;
				if (obj.dTime != dTime && pathPrediction != null)
				{
					pathPrediction.Update(ref obj);
				}
				if (objPath != null)
				{
					objPath.AddPosition(ref obj);
				}

				objPos.Longitude.Value = (float)obj.dLongitude;
				objPos.Latitude.Value = (float)obj.dLatitude;
				objPos.Altitude.Value = (float)obj.dAltitude;
				strTitle.Value = obj.szTitle;
				strATCType.Value = obj.szATCType;
				strATCModel.Value = obj.szATCModel;
				strATCID.Value = obj.szATCID;
				strATCAirline.Value = obj.szATCAirline;
				strATCFlightNumber.Value = obj.szATCFlightNumber;
				fHeading.Value = (float)obj.dHeading;
				dTime = obj.dTime;
				if (State == STATE.DATAREAD || State == STATE.UNCHANGED)
				{
					if (HasMoved || HasChanged)
						State = STATE.MODIFIED;
					else
						State = STATE.UNCHANGED;
				}
				bDataRecieved = true;
			}

			public void ConfigChanged()
			{
				bool bPath = false;
				bool bPrediction = false;
				bool bPredictionPoints = false;
				switch (ObjectType)
				{
					case DATA_REQUESTS.REQUEST_USER_AIRCRAFT:
						bPrediction = Program.Config[Config.SETTING.USER_PATH_PREDICTION]["Enabled"].BoolValue;
						bPredictionPoints = true;
						bPath = Program.Config[Config.SETTING.QUERY_USER_PATH]["Enabled"].BoolValue;
						break;
					case DATA_REQUESTS.REQUEST_AI_PLANE:
						bPrediction = Program.Config[Config.SETTING.QUERY_AI_AIRCRAFTS]["Prediction"].BoolValue;
						bPredictionPoints = Program.Config[Config.SETTING.QUERY_AI_AIRCRAFTS]["PredictionPoints"].BoolValue;
						break;
					case DATA_REQUESTS.REQUEST_AI_HELICOPTER:
						bPrediction = Program.Config[Config.SETTING.QUERY_AI_HELICOPTERS]["Prediction"].BoolValue;
						bPredictionPoints = Program.Config[Config.SETTING.QUERY_AI_HELICOPTERS]["PredictionPoints"].BoolValue;
						break;
					case DATA_REQUESTS.REQUEST_AI_BOAT:
						bPrediction = Program.Config[Config.SETTING.QUERY_AI_BOATS]["Prediction"].BoolValue;
						bPredictionPoints = Program.Config[Config.SETTING.QUERY_AI_BOATS]["PredictionPoints"].BoolValue;
						break;
					case DATA_REQUESTS.REQUEST_AI_GROUND:
						bPrediction = Program.Config[Config.SETTING.QUERY_AI_GROUND_UNITS]["Prediction"].BoolValue;
						bPredictionPoints = Program.Config[Config.SETTING.QUERY_AI_GROUND_UNITS]["PredictionPoints"].BoolValue;
						break;
				}
				if (bPath && objPath == null)
					objPath = new ObjectPath();
				else if (!bPath)
					objPath = null;

				if (bPrediction)
				{
					if (pathPrediction == null)
						pathPrediction = new PathPrediction(bPredictionPoints);
					else
						pathPrediction.HasPoints = bPredictionPoints;
				}
				else
				{
					pathPrediction = null;
				}
			}

			public void ReplaceObjectInfos(ref String str)
			{
				str = str.Replace("%TITLE%", Title.Value);
				str = str.Replace("%ATCTYPE%", ATCType.Value);
				str = str.Replace("%ATCMODEL%", ATCModel.Value);
				str = str.Replace("%ATCID%", ATCID.Value);
				str = str.Replace("%ATCFLIGHTNUMBER%", ATCFlightNumber.Value);
				str = str.Replace("%ATCAIRLINE%", ATCAirline.Value);
				str = str.Replace("%LONGITUDE%", XmlConvert.ToString(ObjectPosition.Longitude.Value));
				str = str.Replace("%LATITUDE%", XmlConvert.ToString(ObjectPosition.Latitude.Value));
				str = str.Replace("%ALTITUDE_UF%", String.Format("{0:F0}ft", ObjectPosition.Altitude.Value * 3.28095));
				str = str.Replace("%ALTITUDE%", XmlConvert.ToString(ObjectPosition.Altitude.Value));
				str = str.Replace("%HEADING%", XmlConvert.ToString(Heading.Value));
				str = str.Replace("%IMAGE%", Program.Config.Server + "/" + Title.Value);
			}

			#region Accessors
			public ObjectData<String> Title
			{
				get
				{
					return strTitle;
				}
			}

			public ObjectData<String> ATCType
			{
				get
				{
					return strATCType;
				}
			}

			public ObjectData<String> ATCModel
			{
				get
				{
					return strATCModel;
				}
			}

			public ObjectData<String> ATCID
			{
				get
				{
					return strATCID;
				}
			}

			public ObjectData<String> ATCAirline
			{
				get
				{
					return strATCAirline;
				}
			}

			public ObjectData<String> ATCFlightNumber
			{
				get
				{
					return strATCFlightNumber;
				}
			}

			public ObjectPosition ObjectPosition
			{
				get
				{
					return objPos;
				}
			}

			public ObjectData<float> Heading
			{
				get
				{
					return fHeading;
				}
			}

			public String Coordinates
			{
				get
				{
					return XmlConvert.ToString(objPos.Longitude.Value) + "," + XmlConvert.ToString(objPos.Latitude.Value) + "," + XmlConvert.ToString(objPos.Altitude.Value);
				}
			}

			public double Time
			{
				get
				{
					return dTime;
				}
			}

			public bool HasMoved
			{
				get
				{
					return objPos.HasMoved || fHeading.IsModified;
				}
			}

			public bool HasChanged
			{
				get
				{
					if (strTitle.IsModified ||
						strATCType.IsModified ||
						strATCModel.IsModified ||
						strATCID.IsModified ||
						strATCAirline.IsModified ||
						strATCFlightNumber.IsModified)
						return true;
					else
						return false;
				}
			}

			#endregion
		}
		public class FlightPlan : SceneryObject
		{
			public class Waypoint
			{
				#region Variables
				private String strName;
				private float fLon;
				private float fLat;
				KmlFactory.KML_ICON_TYPES tIconType;
				#endregion

				public Waypoint(String strName, float fLon, float fLat, KmlFactory.KML_ICON_TYPES tIconType)
				{
					this.strName = strName;
					this.fLon = fLon;
					this.fLat = fLat;
					this.tIconType = tIconType;
				}

				#region Accessors
				public String Name
				{
					get
					{
						return strName;
					}
				}
				public float Longitude
				{
					get
					{
						return fLon;
					}
				}
				public float Latitude
				{
					get
					{
						return fLat;
					}
				}
				public KmlFactory.KML_ICON_TYPES IconType
				{
					get
					{
						return tIconType;
					}
				}
				#endregion
			}

			private List<Waypoint> lstWaypoints;
			private String strName;

			public FlightPlan(uint unID, DATA_REQUESTS tType)
				: base(unID, tType)
			{
				lstWaypoints = new List<Waypoint>();
			}

			public void AddWaypoint(String strName, float fLon, float fLat, KmlFactory.KML_ICON_TYPES tIconType)
			{
				lstWaypoints.Add(new Waypoint(String.Format("Waypoint {0}: {1} ", lstWaypoints.Count + 1, strName), fLon, fLat, tIconType));
			}
			public void AddWaypoint(XmlNode xmln)
			{
				String str;
				KmlFactory.KML_ICON_TYPES tIconType = KmlFactory.KML_ICON_TYPES.NONE;
				String strName = "";
				float fLon = 0;
				float fLat = 0;

				if (xmln.Name != "ATCWaypoint")
					throw new InvalidDataException("XmlNode must have the name ATCWaypoint");

				for (XmlNode node = xmln.FirstChild; node != null; node = node.NextSibling)
				{
					if (node.Name == "ATCWaypointType")
					{
						str = node.InnerText.ToLower();
						if (str == "intersection")
						{
							strName += "Intersection ";
							tIconType = KmlFactory.KML_ICON_TYPES.PLAN_INTER;
						}
						else if (str == "vor")
						{
							strName += "VOR ";
							tIconType = KmlFactory.KML_ICON_TYPES.VOR;
						}
						else if (str == "airport")
						{
							strName += "Airport ";
							tIconType = KmlFactory.KML_ICON_TYPES.AIRPORT;
						}
						else if (str == "ndb")
						{
							strName += "NDB ";
							tIconType = KmlFactory.KML_ICON_TYPES.NDB;
						}
						else if (str == "user")
						{
							strName += "User ";
							tIconType = KmlFactory.KML_ICON_TYPES.PLAN_USER;
						}
						else
						{
							strName += xmln.InnerText + " ";
							tIconType = KmlFactory.KML_ICON_TYPES.NONE;
						}
					}
					else if (node.Name == "WorldPosition")
					{
						String[] strCoords = node.InnerText.Split(',');
						if (strCoords.Length != 3)
							throw new InvalidDataException("Invalid coordinateformat");
						fLat = FsxConnection.ConvertDegToFloat(strCoords[0]);
						fLon = FsxConnection.ConvertDegToFloat(strCoords[1]);
					}
				}
				if (xmln["ICAO"]["ICAOIdent"] != null)
					strName += xmln["ICAO"]["ICAOIdent"].InnerText;
				else if (xmln.Attributes["id"] != null)
					strName += xmln.Attributes["id"].Value;

				AddWaypoint(strName, fLon, fLat, tIconType);
			}

			public String Name
			{
				get
				{
					return strName;
				}
				set
				{
					strName = value;
				}
			}
			public List<Waypoint> Waypoints
			{
				get
				{
					return lstWaypoints;
				}
			}
		}
		public class SceneryDBObject : SceneryObject
		{
            public SceneryDBObject(uint unID, DATA_REQUESTS tType)
				: base(unID, tType)
			{
			}

/*			bool IsInRegion(float fNorth, float fEast, float fSouth, float fWest)
			{
				return fLon >= fWest && fLon <= fEast && fLat >= fNorth && fLat <= fSouth;
			}
*/
		}
		#endregion

		#region Variables
		private FsxgetForm frmMain;
		private IntPtr frmMainHandle;
		private const int WM_USER_SIMCONNECT = 0x0402;
		private System.Timers.Timer timerConnect;
		private uint uiUserAircraftID;
		private SimConnect simconnect;
		public Object lockSimConnect;
		public StructObjectContainer[] objects;
        public Hashtable htFlightPlans;
		private uint unFlightPlanNr;
		private OleDbConnection dbCon;

        #region Morsecodes (0-9 A-Z)
        static String[] strMorseSigns = new String[]
        {
            "-----",
            ".----",
            "..---",
            "...--",
            "....-",
            ".....",
            "-....",
            "--...",
            "---..",
            "----.",
            ".- ",
            "-... ",
            "-.-. ",
            "-.. ",
            ". ",
            "..-. ",
            "--. ",
            ".... ",
            ".. ",
            ".--- ",
            "-.- ",
            ".-.. ",
            "-- ",
            "-. ",
            "--- ",
            ".--. ",
            "--.- ",
            ".-. ",
            "... ",
            "- ",
            "..- ",
            "...- ",
            ".-- ",
            "-..- ",
            "-.-- ",
            "--.. ",
        };
        #endregion
        #endregion

        #region Structs & Enums
        public enum EVENT_ID
		{
			EVENT_MENU,
			EVENT_MENU_START,
			EVENT_MENU_STOP,
			EVENT_MENU_OPTIONS,
			EVENT_MENU_CLEAR_USER_PATH,
			EVENT_SET_NAV1,
			EVENT_SET_NAV2,
			EVENT_SET_ADF,
		};
		public enum GROUP_ID
		{
			GROUP_USER,
		}
		public enum DEFINITIONS
		{
			StructBasicMovingSceneryObject,
		};

		public enum DATA_REQUESTS
		{
			REQUEST_USER_AIRCRAFT = 0,
			REQUEST_AI_HELICOPTER,
			REQUEST_AI_PLANE,
			REQUEST_AI_BOAT,
			REQUEST_AI_GROUND,
			FLIGHTPLAN,
			NAVAIDS,
            AIRPORTS
		};

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
		public struct StructBasicMovingSceneryObject
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public String szTitle;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public String szATCType;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public String szATCModel;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public String szATCID;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
			public String szATCAirline;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public String szATCFlightNumber;
			public double dLatitude;
			public double dLongitude;
			public double dAltitude;
			public double dTime;
			public double dHeading;
		};

		public struct StructObjectContainer
		{
			public Object lockObject;
			public Hashtable htObjects;
			public System.Timers.Timer timer;
			public int nPreAnz;
			public int nPostAnz;
		}
		#endregion

		#region Construction
		public FsxConnection(FsxgetForm frmMain, bool bAddOn)
		{
			this.frmMain = frmMain;
			this.frmMainHandle = frmMain.Handle;
			unFlightPlanNr = 1;
			simconnect = null;
			if (bAddOn)
			{
				if (openConnection())
				{
					AddMenuItems();
				}
			}
			else
			{
				timerConnect = new System.Timers.Timer();
				timerConnect.Interval = 3000;
				timerConnect.Elapsed += new ElapsedEventHandler(OnTimerConnectElapsed);
			}

            objects = new StructObjectContainer[Enum.GetNames(typeof(DATA_REQUESTS)).Length];

            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT] = new StructObjectContainer();
            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].lockObject = new Object();
            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects = new Hashtable();
            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].timer = new System.Timers.Timer();
            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].timer.Elapsed += new ElapsedEventHandler(OnTimerQueryUserAircraftElapsed);

			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE] = new StructObjectContainer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].lockObject = new Object();
			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].htObjects = new Hashtable();
			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].timer = new System.Timers.Timer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].timer.Elapsed += new ElapsedEventHandler(OnTimerQueryAIAircraftsElapsed);

			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER] = new StructObjectContainer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].lockObject = new Object();
			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].htObjects = new Hashtable();
			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].timer = new System.Timers.Timer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].timer.Elapsed += new ElapsedEventHandler(OnTimerQueryAIHelicoptersElapsed);

			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT] = new StructObjectContainer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].lockObject = new Object();
			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].htObjects = new Hashtable();
			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].timer = new System.Timers.Timer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].timer.Elapsed += new ElapsedEventHandler(OntimerQueryAIBoatsElapsed);

			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND] = new StructObjectContainer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].lockObject = new Object();
			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].htObjects = new Hashtable();
			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].timer = new System.Timers.Timer();
			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].timer.Elapsed += new ElapsedEventHandler(OnTimerQueryAIGroundUnitsElapsed);

			objects[(int)DATA_REQUESTS.NAVAIDS] = new StructObjectContainer();
			objects[(int)DATA_REQUESTS.NAVAIDS].lockObject = new Object();
			objects[(int)DATA_REQUESTS.NAVAIDS].htObjects = new Hashtable();
			objects[(int)DATA_REQUESTS.NAVAIDS].timer = new System.Timers.Timer();
			objects[(int)DATA_REQUESTS.NAVAIDS].timer.Elapsed += new ElapsedEventHandler(OnTimerQueryNavAidsElapsed);

            objects[(int)DATA_REQUESTS.AIRPORTS] = new StructObjectContainer();
            objects[(int)DATA_REQUESTS.AIRPORTS].lockObject = new Object();
            objects[(int)DATA_REQUESTS.AIRPORTS].htObjects = new Hashtable();
            objects[(int)DATA_REQUESTS.AIRPORTS].timer = new System.Timers.Timer();
            objects[(int)DATA_REQUESTS.AIRPORTS].timer.Elapsed += new ElapsedEventHandler(OnTimerQueryAirportsElapsed);
            
			lockSimConnect = new Object();

			htFlightPlans = new Hashtable();

			dbCon = new OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + Program.Config.AppPath + "\\data\\fsxget.mdb");
			dbCon.Open();
		}

		#endregion

		#region FSX-Handling
		public void Connect()
		{
			lock (lockSimConnect)
			{
				if (simconnect == null)
					timerConnect.Start();
			}
		}
		public void Disconnect()
		{
			closeConnection();
			timerConnect.Stop();
		}
		private bool openConnection()
		{
			if (simconnect == null)
			{
				try
				{
					simconnect = new SimConnect(frmMain.Text, frmMainHandle, WM_USER_SIMCONNECT, null, 0);
					if (initDataRequest())
					{
						InitializeTimers();
						return true;
					}
					else
						return false;
				}
				catch
				{
					return false;
				}
			}
			else
				return false;
		}

        private bool initDataRequest()
		{
			try
			{
				// listen to connect and quit msgs
				simconnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(simconnect_OnRecvOpen);
				simconnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(simconnect_OnRecvQuit);
				simconnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(simconnect_OnRecvEvent);

				// listen to exceptions
				simconnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(simconnect_OnRecvException);

				// define a data structure
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "Title", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "ATC Type", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "ATC Model", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "ATC ID", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "ATC Airline", null, SIMCONNECT_DATATYPE.STRING64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "ATC Flight Number", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "Plane Latitude", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "Plane Longitude", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "Plane Altitude", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "Absolute Time", "seconds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
				simconnect.AddToDataDefinition(DEFINITIONS.StructBasicMovingSceneryObject, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

				simconnect.MapClientEventToSimEvent(EVENT_ID.EVENT_SET_NAV1, "NAV1_RADIO_SET");
				simconnect.MapClientEventToSimEvent(EVENT_ID.EVENT_SET_NAV2, "NAV2_RADIO_SET");
				simconnect.MapClientEventToSimEvent(EVENT_ID.EVENT_SET_ADF, "ADF_SET");
				//                simconnect.SetNotificationGroupPriority(GROUP_ID.GROUP_USER, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);
				// IMPORTANT: register it with the simconnect managed wrapper marshaller
				// if you skip this step, you will only receive a uint in the .dwData field.
				simconnect.RegisterDataDefineStruct<StructBasicMovingSceneryObject>(DEFINITIONS.StructBasicMovingSceneryObject);

				// catch a simobject data request
				simconnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(simconnect_OnRecvSimobjectDataBytype);

				return true;
			}
			catch (COMException ex)
			{
				frmMain.NotifyError("FSX Exception!\n\n" + ex.Message);
				return false;
			}
		}
		private void AddMenuItems()
		{
			if (simconnect != null)
			{
				try
				{
					simconnect.MenuAddItem(frmMain.Text, EVENT_ID.EVENT_MENU, 0);
					simconnect.MenuAddSubItem(EVENT_ID.EVENT_MENU, "&Start", EVENT_ID.EVENT_MENU_START, 0);
					simconnect.MenuAddSubItem(EVENT_ID.EVENT_MENU, "Sto&p", EVENT_ID.EVENT_MENU_STOP, 0);
					simconnect.MenuAddSubItem(EVENT_ID.EVENT_MENU, "&Options", EVENT_ID.EVENT_MENU_OPTIONS, 0);
					simconnect.MenuAddSubItem(EVENT_ID.EVENT_MENU, "&Clear User Aircraft Path", EVENT_ID.EVENT_MENU_CLEAR_USER_PATH, 0);
				}
				catch (COMException e)
				{
					frmMain.NotifyError("FSX Add MenuItem failed!\n\n" + e.Message);
				}
			}
		}
		private void simconnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
		{

		}
		private void simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
		{
			DeleteAllObjects();
			closeConnection();
			frmMain.Connected = false;
			if (timerConnect != null)
				timerConnect.Start();
			else
				frmMain.Close();
		}
		private void simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
		{
			frmMain.NotifyError("FSX Exception!");
		}
		private void simconnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
		{
			switch ((EVENT_ID)data.uEventID)
			{
				case EVENT_ID.EVENT_MENU_OPTIONS:
					frmMain.Show();
					break;
			}
		}
		private void simconnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
		{
			StructBasicMovingSceneryObject obj = (StructBasicMovingSceneryObject)data.dwData[0];

			switch ((DATA_REQUESTS)data.dwRequestID)
			{
				case DATA_REQUESTS.REQUEST_USER_AIRCRAFT:
                    lock (objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].lockObject)
					{
                        if (objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects.ContainsKey(data.dwObjectID))
						{
							
                            ((SceneryMovingObject)objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects[data.dwObjectID]).Update(ref obj);
							uiUserAircraftID = data.dwObjectID;
						}
						else
                            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects.Add( data.dwObjectID, new SceneryMovingObject(data.dwObjectID, DATA_REQUESTS.REQUEST_USER_AIRCRAFT, ref obj));
					}
					break;
				case DATA_REQUESTS.REQUEST_AI_PLANE:
					lock (objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].lockObject)
					{
						if (data.dwObjectID != uiUserAircraftID)
						{
							HandleSimObjectRecieved(ref objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE], ref data);
						}
					}
					break;
				case DATA_REQUESTS.REQUEST_AI_HELICOPTER:
					lock (objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].lockObject)
					{
						if (data.dwObjectID != uiUserAircraftID)
						{
							HandleSimObjectRecieved(ref objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER], ref data);
						}
					}
					break;
				case DATA_REQUESTS.REQUEST_AI_BOAT:
					lock (objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].lockObject)
					{
						HandleSimObjectRecieved(ref objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT], ref data);
					}
					break;
				case DATA_REQUESTS.REQUEST_AI_GROUND:
					lock (objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].lockObject)
					{
						HandleSimObjectRecieved(ref objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND], ref data);
					}
					break;
				default:
#if DEBUG
					frmMain.NotifyError("Received unknown data from FSX!");
#endif
					break;
			}
		}
		private void HandleSimObjectRecieved(ref StructObjectContainer objs, ref SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
		{
			StructBasicMovingSceneryObject obj = (StructBasicMovingSceneryObject)data.dwData[0];
			if (data.dwoutof == 0)
				MarkDeletedObjects(ref objs.htObjects);
			else
			{
				if (data.dwentrynumber == 1)
				{
					objs.nPreAnz = objs.htObjects.Count;
					objs.nPostAnz = 0;
				}
				if (objs.htObjects.ContainsKey(data.dwObjectID))
				{
					((SceneryMovingObject)objs.htObjects[data.dwObjectID]).Update(ref obj);
					objs.nPostAnz++;
				}
				else
				{
					objs.htObjects.Add(data.dwObjectID, new SceneryMovingObject(data.dwObjectID, (DATA_REQUESTS)data.dwRequestID, ref obj));
				}
				if (data.dwentrynumber == data.dwoutof && objs.nPostAnz < objs.nPreAnz)
				{
					MarkDeletedObjects(ref objs.htObjects);
				}
			}
		}
		private void MarkDeletedObjects(ref Hashtable ht)
		{
			foreach (DictionaryEntry entry in ht)
			{
				if (!((SceneryObject)entry.Value).bDataRecieved)
				{
					((SceneryObject)entry.Value).State = SceneryMovingObject.STATE.DELETED;
				}
				((SceneryObject)entry.Value).bDataRecieved = false;
			}
		}

        public void closeConnection()
        {
            lock (lockSimConnect)
            {
                if (simconnect != null)
                {
                    EnableTimers(false);
                    DeleteAllObjects();
                    frmMain.Connected = false;
                    simconnect.Dispose();
                    simconnect = null;
                }
            }
        }
        public void DeleteAllObjects()
		{
            foreach (DATA_REQUESTS request in Enum.GetValues(typeof(DATA_REQUESTS)))
            {
                lock (objects[(int)request].lockObject)
                {
                    foreach (DictionaryEntry entry in objects[(int)request].htObjects)
                    {
                        ((SceneryObject)(entry.Value)).State = SceneryObject.STATE.DELETED;
                    }
                }
            }
		}
		public void CleanupHashtable(ref Hashtable ht)
		{
			ArrayList toDel = new ArrayList();
			foreach (DictionaryEntry entry in ht)
			{
				if (((SceneryObject)entry.Value).State == SceneryObject.STATE.DELETED)
				{
					toDel.Add(entry.Key);
				}
			}
			foreach (object key in toDel)
			{
				ht.Remove(key);
			}
		}
		public bool SetFrequency(String strType, double dFreq)
		{
			bool bRet = true;
			try
			{
				strType = strType.ToLower();
				if (strType == "nav1")
				{

					simconnect.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, EVENT_ID.EVENT_SET_NAV1, UIntToBCD((uint)(dFreq * 100)), GROUP_ID.GROUP_USER, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
				}
				else if (strType == "nav2")
					simconnect.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, EVENT_ID.EVENT_SET_NAV2, UIntToBCD((uint)(dFreq * 100)), GROUP_ID.GROUP_USER, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
				else if (strType == "adf")
				{
					simconnect.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, EVENT_ID.EVENT_SET_ADF, UIntToBCD((uint)(dFreq)), GROUP_ID.GROUP_USER, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
				}
				else
					bRet = false;
			}
			catch
			{
				bRet = false;
			}
			return bRet;
		}
        public void AddFlightPlan(String strFileName)
        {
            try
            {
                XmlDocument xmld = new XmlDocument();
                xmld.Load(strFileName);

                FlightPlan flightPlan = new FlightPlan(unFlightPlanNr, DATA_REQUESTS.FLIGHTPLAN);

                XmlElement xmle = xmld["SimBase.Document"]["FlightPlan.FlightPlan"];
                if (xmle == null)
                    throw new InvalidDataException("This is not a FSX flightplan");

                xmle = xmle["Title"];
                if (xmle != null)
                    flightPlan.Name = xmle.InnerText;
                else
                    flightPlan.Name = "Flightplan";

                xmle = xmle.ParentNode["FPType"];
                if (xmle != null)
                    flightPlan.Name += " (" + xmle.InnerText + ")";

                XmlNodeList xmlnWP = xmld.GetElementsByTagName("ATCWaypoint");
                foreach (XmlNode xmln in xmlnWP)
                {
                    flightPlan.AddWaypoint(xmln);
                }
                htFlightPlans.Add(unFlightPlanNr++, flightPlan);
            }
            catch
            {
                frmMain.NotifyError("Can not load the flight plan");
            }
        }
		public bool OnMessageReceive(ref System.Windows.Forms.Message m)
		{
			if (m.Msg == WM_USER_SIMCONNECT)
			{
				if (simconnect != null)
				{
					try
					{
						simconnect.ReceiveMessage();
					}
					catch (Exception e)
					{
#if DEBUG
						frmMain.NotifyError("Error receiving data from FSX!\n\n" + e.Message);
#endif
					}
				}
				return true;
			}

			return false;
		}
		#endregion

		#region Timerfunctions
		public void InitializeTimers()
		{
            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].timer.Stop();
            objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].timer.Interval = Program.Config[Config.SETTING.QUERY_USER_AIRCRAFT]["Interval"].IntValue;

			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].timer.Stop();
			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].timer.Interval = Program.Config[Config.SETTING.QUERY_AI_AIRCRAFTS]["Interval"].IntValue;

			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].timer.Stop();
			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].timer.Interval = Program.Config[Config.SETTING.QUERY_AI_HELICOPTERS]["Interval"].IntValue;

			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].timer.Stop();
			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].timer.Interval = Program.Config[Config.SETTING.QUERY_AI_BOATS]["Interval"].IntValue;

			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].timer.Stop();
			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].timer.Interval = Program.Config[Config.SETTING.QUERY_AI_GROUND_UNITS]["Interval"].IntValue;

			objects[(int)DATA_REQUESTS.NAVAIDS].timer.Stop();
			objects[(int)DATA_REQUESTS.NAVAIDS].timer.Interval = Program.Config[Config.SETTING.QUERY_NAVAIDS]["Interval"].IntValue * 1000;

            objects[(int)DATA_REQUESTS.AIRPORTS].timer.Stop();
            objects[(int)DATA_REQUESTS.AIRPORTS].timer.Interval = Program.Config[Config.SETTING.QUERY_NAVAIDS]["Interval"].IntValue * 1000;

			EnableTimers();
		}
		public void EnableTimers()
		{
			EnableTimers(true);
		}
		public void EnableTimers(bool bEnable)
		{
			bool bQueryAI = Program.Config[Config.SETTING.QUERY_AI_OBJECTS]["Enabled"].BoolValue;
			objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].timer.Enabled = bEnable && Program.Config[Config.SETTING.QUERY_USER_AIRCRAFT]["Enabled"].BoolValue;
			objects[(int)DATA_REQUESTS.REQUEST_AI_PLANE].timer.Enabled = bEnable && bQueryAI && Program.Config[Config.SETTING.QUERY_AI_AIRCRAFTS]["Enabled"].BoolValue;
			objects[(int)DATA_REQUESTS.REQUEST_AI_HELICOPTER].timer.Enabled = bEnable && bQueryAI && Program.Config[Config.SETTING.QUERY_AI_HELICOPTERS]["Enabled"].BoolValue;
			objects[(int)DATA_REQUESTS.REQUEST_AI_BOAT].timer.Enabled = bEnable && bQueryAI && Program.Config[Config.SETTING.QUERY_AI_BOATS]["Enabled"].BoolValue;
			objects[(int)DATA_REQUESTS.REQUEST_AI_GROUND].timer.Enabled = bEnable && bQueryAI && Program.Config[Config.SETTING.QUERY_AI_GROUND_UNITS]["Enabled"].BoolValue;
			objects[(int)DATA_REQUESTS.NAVAIDS].timer.Enabled = bEnable && Program.Config[Config.SETTING.QUERY_NAVAIDS]["Enabled"].BoolValue;
            objects[(int)DATA_REQUESTS.AIRPORTS].timer.Enabled = bEnable && Program.Config[Config.SETTING.QUERY_NAVAIDS]["Enabled"].BoolValue;
        }

		private void OnTimerConnectElapsed(object sender, ElapsedEventArgs e)
		{
			if (openConnection())
			{
				frmMain.Connected = true;
				timerConnect.Stop();
			}
		}
		private void OnTimerQueryUserAircraftElapsed(object sender, ElapsedEventArgs e)
		{
			lock (lockSimConnect)
			{
				try
				{
					simconnect.RequestDataOnSimObjectType(DATA_REQUESTS.REQUEST_USER_AIRCRAFT, DEFINITIONS.StructBasicMovingSceneryObject, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);
				}
				catch
				{
				}
			}
		}
		private void OnTimerQueryAIAircraftsElapsed(object sender, ElapsedEventArgs e)
		{
			lock (lockSimConnect)
			{
				try
				{
					simconnect.RequestDataOnSimObjectType(DATA_REQUESTS.REQUEST_AI_PLANE, DEFINITIONS.StructBasicMovingSceneryObject, (uint)Program.Config[Config.SETTING.QUERY_AI_AIRCRAFTS]["Range"].IntValue, SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT);

				}
				catch (COMException ex)
				{
					frmMain.NotifyError(ex.Message);
				}
			}
		}
		private void OnTimerQueryAIHelicoptersElapsed(object sender, ElapsedEventArgs e)
		{
			lock (lockSimConnect)
			{
				try
				{
					simconnect.RequestDataOnSimObjectType(DATA_REQUESTS.REQUEST_AI_HELICOPTER, DEFINITIONS.StructBasicMovingSceneryObject, (uint)Program.Config[Config.SETTING.QUERY_AI_HELICOPTERS]["Range"].IntValue, SIMCONNECT_SIMOBJECT_TYPE.HELICOPTER);
				}
				catch
				{
				}
			}
		}
		private void OntimerQueryAIBoatsElapsed(object sender, ElapsedEventArgs e)
		{
			lock (lockSimConnect)
			{
				try
				{
					simconnect.RequestDataOnSimObjectType(DATA_REQUESTS.REQUEST_AI_BOAT, DEFINITIONS.StructBasicMovingSceneryObject, (uint)Program.Config[Config.SETTING.QUERY_AI_BOATS]["Range"].IntValue, SIMCONNECT_SIMOBJECT_TYPE.BOAT);
				}
				catch
				{
				}
			}
		}
		private void OnTimerQueryAIGroundUnitsElapsed(object sender, ElapsedEventArgs e)
		{
			lock (lockSimConnect)
			{
				try
				{
					simconnect.RequestDataOnSimObjectType(DATA_REQUESTS.REQUEST_AI_GROUND, DEFINITIONS.StructBasicMovingSceneryObject, (uint)Program.Config[Config.SETTING.QUERY_AI_GROUND_UNITS]["Range"].IntValue, SIMCONNECT_SIMOBJECT_TYPE.GROUND);
				}
				catch
				{
				}
			}
		}
		private void OnTimerQueryNavAidsElapsed(object sender, ElapsedEventArgs e)
		{
			lock (objects[(int)DATA_REQUESTS.NAVAIDS].lockObject)
			{
				if (objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects.Count > 0 )
				{
					float fNorth = 0;
					float fEast = 0;
					float fSouth = 0;
					float fWest = 0;
					float fTmp = 0;
                    KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 0, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fTmp, ref fNorth);
					KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 90, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fEast, ref fTmp);
					KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 180, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fTmp, ref fSouth);
					KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 270, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fWest, ref fTmp);

					OleDbCommand cmd = new OleDbCommand("SELECT ID FROM navaids WHERE " +
						"Latitude >= " + fSouth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + " AND " +
						"Latitude <= " + fNorth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + " AND " +
						"Longitude >= " + fWest.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + " AND " +
						"Longitude <= " + fEast.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + ";", dbCon);

					OleDbDataReader rd = cmd.ExecuteReader();
					while (rd.Read())
					{
						uint unID = (uint)rd.GetInt32(0);
						if (objects[(int)DATA_REQUESTS.NAVAIDS].htObjects.ContainsKey(unID))
						{
							((SceneryObject)objects[(int)DATA_REQUESTS.NAVAIDS].htObjects[unID]).bDataRecieved = true;
						}
						else
						{
							objects[(int)DATA_REQUESTS.NAVAIDS].htObjects.Add(unID, new SceneryDBObject( unID, DATA_REQUESTS.NAVAIDS));
						}
					}
					MarkDeletedObjects(ref objects[(int)DATA_REQUESTS.NAVAIDS].htObjects);
					rd.Close();
				}
			}
		}
        private void OnTimerQueryAirportsElapsed(object sender, ElapsedEventArgs e)
        {
            lock (objects[(int)DATA_REQUESTS.AIRPORTS].lockObject)
            {
                if (objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects.Count > 0)
                {
                    float fNorth = 0;
                    float fEast = 0;
                    float fSouth = 0;
                    float fWest = 0;
                    float fTmp = 0;
                    SceneryMovingObject UserAircraft = (SceneryMovingObject)objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects[uiUserAircraftID];
                    KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 0, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fTmp, ref fNorth);
                    KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 90, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fEast, ref fTmp);
                    KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 180, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fTmp, ref fSouth);
                    KmlFactory.MovePoint(UserAircraft.ObjectPosition.Longitude.Value, UserAircraft.ObjectPosition.Latitude.Value, 270, Program.Config[Config.SETTING.QUERY_NAVAIDS]["Range"].IntValue, ref fWest, ref fTmp);

                    OleDbCommand cmd = new OleDbCommand("SELECT ID FROM airports WHERE " +
                        "Latitude >= " + fSouth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + " AND " +
                        "Latitude <= " + fNorth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + " AND " +
                        "Longitude >= " + fWest.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + " AND " +
                        "Longitude <= " + fEast.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + ";", dbCon);

                    OleDbDataReader rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        uint unID = (uint)rd.GetInt32(0);
                        if (objects[(int)DATA_REQUESTS.AIRPORTS].htObjects.ContainsKey(unID))
                        {
                            ((SceneryObject)objects[(int)DATA_REQUESTS.AIRPORTS].htObjects[unID]).bDataRecieved = true;
                        }
                        else
                        {
                            objects[(int)DATA_REQUESTS.AIRPORTS].htObjects.Add(unID, new SceneryDBObject(unID, DATA_REQUESTS.AIRPORTS));
                        }
                    }
                    MarkDeletedObjects(ref objects[(int)DATA_REQUESTS.AIRPORTS].htObjects);
                    rd.Close();
                }
            }
        }
        #endregion

        public SceneryMovingObject UserAircraft
        {
            get
            {
                if (objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects.ContainsKey(uiUserAircraftID))
                    return (SceneryMovingObject)objects[(int)DATA_REQUESTS.REQUEST_USER_AIRCRAFT].htObjects[uiUserAircraftID];
                else
                    return null;
            }
        }

        #region Static Helperfunctions
        static public String GetMorseCode(String str)
		{
			str = str.ToUpper();
			String strMorseCode = "";
			for (int i = 0; i < str.Length; i++)
			{
				if (str[i] >= 'A' && str[i] <= 'Z')
					strMorseCode += strMorseSigns[str[i] - 'A' + 10];
				else if (str[i] >= '0' && str[i] <= '9')
					strMorseCode += strMorseSigns[str[i] - '0'];
				else
					strMorseCode += "? ";
			}
			return strMorseCode;
		}
		static public String GetRegionName(String strICAORegionCode)
		{
			String strRegion = "Unbekannt";
			if (strICAORegionCode != null && strICAORegionCode.Length >= 1)
			{
				switch (strICAORegionCode[0])
				{
					case 'A':
						strRegion = "S�dwest-Pazifik";
						break;
					case 'B':
						strRegion = "Polarregion";
						break;
					case 'C':
						strRegion = "Kanada";
						break;
					case 'D':
						strRegion = "Westafrika";
						break;
					case 'E':
						strRegion = "Nordeuropa";
						break;
					case 'F':
						strRegion = "S�dafrika";
						break;
					case 'G':
						strRegion = "Westafrikanische K�ste";
						break;
					case 'H':
						strRegion = "Ostafrika";
						break;
					case 'K':
						strRegion = "USA";
						break;
					case 'L':
						strRegion = "S�deuropa";
						break;
					case 'M':
						strRegion = "Zentralamerika";
						break;
					case 'N':
						strRegion = "S�dpazifik";
						break;
					case 'O':
						strRegion = "Naher Osten";
						break;
					case 'P':
						strRegion = "N�rdlicher Pazifik";
						break;
					case 'R':
						strRegion = "Ostasien";
						break;
					case 'S':
						strRegion = "S�damerika";
						break;
					case 'T':
						strRegion = "Karibik";
						break;
					case 'U':
						strRegion = "Russische F�deration";
						break;
					case 'V':
						strRegion = "S�dasien";
						break;
					case 'W':
						strRegion = "S�dostasien";
						break;
					case 'Y':
						strRegion = "Australien";
						break;
					case 'Z':
						strRegion = "China";
						break;
				}
			}
			return strRegion;
		}
		static public float ConvertDegToFloat(String szDeg)
		{

			String szTemp = szDeg;

			szTemp = szTemp.Replace("N", "+");
			szTemp = szTemp.Replace("S", "-");
			szTemp = szTemp.Replace("E", "+");
			szTemp = szTemp.Replace("W", "-");

			szTemp = szTemp.Replace(" ", "");

			szTemp = szTemp.Replace("\"", "");
			szTemp = szTemp.Replace("'", "/");
			szTemp = szTemp.Replace("�", "/");

			char[] szSeperator = { '/' };
			String[] szParts = szTemp.Split(szSeperator);

			if (szParts.GetLength(0) != 3)
			{
				throw new System.Exception("Wrong coordinate format!");
			}


			float f1 = float.Parse(szParts[0], System.Globalization.NumberFormatInfo.InvariantInfo);
			int iSign = Math.Sign(f1);
			f1 = Math.Abs(f1);
			float f2 = float.Parse(szParts[1], System.Globalization.NumberFormatInfo.InvariantInfo);
			float f3 = float.Parse(szParts[2], System.Globalization.NumberFormatInfo.InvariantInfo);

			return (float)(iSign * (f1 + (f2 * 60.0 + f3) / 3600.0));
		}
        static public float ConvertDegToFloat2(String szDeg)
        {

            String szTemp = szDeg;

            szTemp = szTemp.Replace("N", "+");
            szTemp = szTemp.Replace("S", "-");
            szTemp = szTemp.Replace("E", "+");
            szTemp = szTemp.Replace("W", "-");

            char[] szSeperator = { ' ' };
            String[] szParts = szTemp.Split(szSeperator);

            if (szParts.GetLength(0) != 2)
            {
                throw new System.Exception("Wrong coordinate format!");
            }


            float f1 = float.Parse(szParts[0], System.Globalization.NumberFormatInfo.InvariantInfo);
            int iSign = Math.Sign(f1);
            f1 = Math.Abs(f1);
            float f2 = float.Parse(szParts[1], System.Globalization.NumberFormatInfo.InvariantInfo);

            return (float)(iSign * (f1 + f2/60));
        }
        static uint UIntToBCD(uint nData)
		{
			String str = nData.ToString();
			nData = 0;
			for (int i = 0; i < str.Length; i++)
			{
				nData *= 16;
				nData += (uint)(str[i] - '0');
			}
			return nData;
        }

        static public Bitmap RenderTaxiwaySign(String strSign)
        {
            List<String> strSegments = new List<String>();
            String strTypeChars = "ldmiru";
            String strAllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            String strSpecialChars = "_ ->^'<v`/\\[]x#=.|";
            int nPos = 0;
            int nPosEnd = 0;
            while ((nPos = strSign.IndexOfAny(strTypeChars.ToCharArray(), nPos)) > -1)
            {
                nPosEnd = strSign.IndexOfAny(strTypeChars.ToCharArray(), nPos + 1);
                if (nPosEnd == -1)
                    nPosEnd = strSign.Length;
                strSegments.Add(strSign.Substring(nPos, nPosEnd - nPos));
                nPos = nPosEnd;
            }

            Font fnt = new Font("Arial", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            Pen pen = null;
            Brush brush = null;
            Color colFG = Color.White;
            Color colBG = Color.White;

            int nHeight = 40;
            int nXBorderStart = 0;
            int nXOff = 4;
            int nYOff = 8;
            int nYMid = nHeight / 2;
            int nArrowWidth = 16;
            int nArrowWidth2 = nArrowWidth / 2;
            int nArrowSpace = 4;

            int nWidth = strSign.Length * TextRenderer.MeasureText("W", fnt).Width;

            Bitmap bmpTmp = new Bitmap(nWidth, nHeight);
            Graphics g = Graphics.FromImage(bmpTmp);
            foreach (String strSeg in strSegments)
            {
                switch (strSeg[0])
                {
                    case 'l':
                        colBG = Color.Black;
                        colFG = Color.Yellow;
                        break;
                    case 'd':
                        colBG = Color.Yellow;
                        colFG = Color.Black;
                        break;
                    case 'm':
                    case 'r':
                        colBG = Color.Red;
                        colFG = Color.White;
                        break;
                    case 'i':
                    case 'u':
                        colBG = Color.White;
                        colFG = Color.Black;
                        break;
                }
                brush = new SolidBrush(colBG);
                pen = new Pen(colFG, 3);
                g.FillRectangle(brush, nXOff, 4, nWidth - nXOff, nHeight);
                for (int i = 1; i < strSeg.Length; i++)
                {
                    if (strAllowedChars.IndexOf(strSeg[i]) > -1)
                    {
                        String str = "";
                        do
                        {
                            str += strSeg[i++];
                        } while (i < str.Length && strAllowedChars.IndexOf(strSeg[i]) > -1);
                        i--;
                        TextRenderer.DrawText(g, str, fnt, new Point(nXOff, nYOff), colFG, colBG);
                        nXOff += TextRenderer.MeasureText(str, fnt).Width;
                    }
                    else if (strSpecialChars.IndexOf(strSeg[i]) > -1)
                    {
                        switch (strSeg[i])
                        {
                            case ' ':
                            case '_':
                                nXOff += TextRenderer.MeasureText(" ", fnt).Width;
                                break;
                            case '-':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff, nYMid, nXOff + nArrowWidth, nYMid);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '>':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff, nYMid, nXOff + nArrowWidth, nYMid);
                                g.DrawLine(pen, nXOff + nArrowWidth2 - 1, nYMid - nArrowWidth2, nXOff + nArrowWidth, nYMid + 1);
                                g.DrawLine(pen, nXOff + nArrowWidth2 - 1, nYMid + nArrowWidth2, nXOff + nArrowWidth, nYMid - 1);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '^':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff + nArrowWidth2, nYMid - nArrowWidth2, nXOff + nArrowWidth2, nYMid + nArrowWidth2);
                                g.DrawLine(pen, nXOff, nYMid, nXOff + nArrowWidth2 + 1, nYMid - nArrowWidth2 - 1);
                                g.DrawLine(pen, nXOff + nArrowWidth, nYMid, nXOff + nArrowWidth2, nYMid - nArrowWidth2);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '\'':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff + nArrowWidth2, nYMid - nArrowWidth2, nXOff + nArrowWidth + 2, nYMid - nArrowWidth2);
                                g.DrawLine(pen, nXOff + nArrowWidth, nYMid - nArrowWidth2, nXOff + nArrowWidth, nYMid);
                                g.DrawLine(pen, nXOff, nYMid + nArrowWidth2, nXOff + nArrowWidth, nYMid - nArrowWidth2);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '<':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff, nYMid, nXOff + nArrowSpace + nArrowWidth, nYMid);
                                g.DrawLine(pen, nXOff + nArrowWidth2 + 1, nYMid - nArrowWidth2, nXOff, nYMid + 1);
                                g.DrawLine(pen, nXOff + nArrowWidth2 + 1, nYMid + nArrowWidth2, nXOff, nYMid - 1);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case 'v':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff + nArrowWidth2, nYMid - nArrowWidth2, nXOff + nArrowWidth2, nYMid + nArrowWidth2);
                                g.DrawLine(pen, nXOff, nYMid, nXOff + nArrowWidth2 + 1, nYMid + nArrowWidth2 + 1);
                                g.DrawLine(pen, nXOff + nArrowWidth, nYMid, nXOff + nArrowWidth2, nYMid + nArrowWidth2);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '`':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff, nYMid - nArrowWidth2, nXOff, nYMid);
                                g.DrawLine(pen, nXOff - 1, nYMid - nArrowWidth2, nXOff + nArrowWidth2, nYMid - nArrowWidth2);
                                g.DrawLine(pen, nXOff, nYMid - nArrowWidth2, nXOff + nArrowWidth, nYMid + nArrowWidth2);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '/':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff, nYMid + nArrowWidth2 + 2, nXOff, nYMid);
                                g.DrawLine(pen, nXOff, nYMid + nArrowWidth2, nXOff + nArrowWidth2, nYMid + nArrowWidth2);
                                g.DrawLine(pen, nXOff, nYMid + nArrowWidth2, nXOff + nArrowWidth, nYMid - nArrowWidth2);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '\\':
                                nXOff += nArrowSpace;
                                g.DrawLine(pen, nXOff + nArrowWidth2, nYMid + nArrowWidth2, nXOff + nArrowWidth, nYMid + nArrowWidth2);
                                g.DrawLine(pen, nXOff + nArrowWidth, nYMid, nXOff + nArrowWidth, nYMid + nArrowWidth2 + 2);
                                g.DrawLine(pen, nXOff, nYMid - nArrowWidth2, nXOff + nArrowWidth, nYMid + nArrowWidth2);
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '[':
                                nXOff += nArrowSpace;
                                nXBorderStart = nXOff;
                                nXOff += nArrowSpace;
                                break;
                            case ']':
                                nXOff += nArrowSpace;
                                g.DrawRectangle(new Pen(colFG, 2), nXBorderStart, nArrowSpace + 4, nXOff - nXBorderStart, nHeight - 2 * nArrowSpace - 8);
                                nXOff += nArrowSpace;
                                break;
                            case 'x':
                                break;
                            case '=':
                                break;
                            case '#':
                                break;
                            case '.':
                                nXOff += nArrowSpace;
                                g.FillEllipse(new SolidBrush(colFG), new Rectangle(nXOff + nArrowWidth2 - 1, nYMid - 1, 3, 3));
                                nXOff += nArrowSpace + nArrowWidth;
                                break;
                            case '|':
                                nXOff += nArrowSpace;
                                g.DrawLine(new Pen(colFG, 2), nXOff, nYMid - nArrowWidth2, nXOff, nYMid + nArrowWidth2);
                                nXOff += nArrowSpace;
                                break;
                        }
                    }
                    else
                        throw new InvalidDataException("Invalid Taxiwaysign-Description");
                }
            }
            nXOff += 4;

            brush = new SolidBrush(Color.LightGray);
            g.FillRectangle(brush, 0, 0, nXOff, 4);
            g.FillRectangle(brush, 0, 0, 4, nHeight);
            g.FillRectangle(brush, nXOff - 4, 0, 4, nHeight);
            g.FillRectangle(brush, 0, nHeight - 4, nXOff, 4);

            Bitmap bmp = new Bitmap(nXOff, nHeight);
            g = Graphics.FromImage(bmp);
            g.DrawImage(bmpTmp, 0, 0);

            bmpTmp = new Bitmap(nXOff, nHeight * 2);

            Point[] ptDest = new Point[] {
                new Point( nXOff, nHeight ),
                new Point( 0, nHeight ),
                new Point( nXOff, 0 ),
            };

            g = Graphics.FromImage(bmpTmp);

            g.DrawImage(bmp, 0, nHeight);
            g.DrawImage(bmp, ptDest);

            return bmpTmp;
        }
        #endregion

        #region Database-Creation
        static String[] strComTypes = new String[] {
            "APPROACH",
            "ASOS",
            "ATIS",
            "AWOS",
            "CENTER",
            "CLEARANCE",
            "CLEARANCE_PRE_TAXI",
            "CTAF",
            "DEPARTURE",
            "FSS",
            "GROUND",
            "MULTICOM",
            "REMOTE_CLEARANCE_DELIVERY",
            "TOWER",
            "UNICOM"
        };

        static String[] strSurfaces = new String[] {
            "ASPHALT",
            "BITUMINOUS",
            "BRICK",
            "CLAY",
            "CEMENT",
            "CONCRETE",
            "CORAL",
            "DIRT",
            "GRASS",
            "GRAVEL",
            "ICE",
            "MACADAM",
            "OIL_TREATED",
            "SAND",
            "SHALE",
            "SNOW",
            "STEEL_MATS",
            "TARMAC",
            "UNKNWON",
            "WATER",
            "PLANKS",
        };

        static String[] strTaxiPointTypes = new String[] {
            "NORMAL",
            "HOLD_SHORT",
            "ILS_HOLD_SHORT",
            "HOLD_SHORT_NO_DRAW",
            "ILS_HOLD_SHORT_NO_DRAW",
        };

        static String[] strTaxiwayParkingNames = new String[] {
            "PARKING",
            "DOCK",
            "GATE",
            "GATE_A",
            "GATE_B",
            "GATE_C",
            "GATE_D",
            "GATE_E",
            "GATE_F",
            "GATE_G",
            "GATE_H",
            "GATE_I",
            "GATE_J",
            "GATE_K",
            "GATE_L",
            "GATE_M",
            "GATE_N",
            "GATE_O",
            "GATE_P",
            "GATE_Q",
            "GATE_R",
            "GATE_S",
            "GATE_T",
            "GATE_U",
            "GATE_V",
            "GATE_W",
            "GATE_X",
            "GATE_Y",
            "GATE_Z",
            "NONE",
            "N_PARKING",
            "NE_PARKING",
            "NW_PARKING",
            "SE_PARKING",
            "S_PARKING",
            "SW_PARKING",
            "W_PARKING",
            "E_PARKING",
        };

        static String[] strTaxiwayParkingTypes = new String[] {
            "NONE",
            "DOCK_GA",
            "FUEL",
            "GATE_HEAVY",
            "GATE_MEDIUM",
            "GATE_SMALL",
            "RAMP_CARGO",
            "RAMP_GA",
            "RAMP_GA_LARGE",
            "RAMP_GA_MEDIUM",
            "RAMP_GA_SMALL",
            "RAMP_MIL_CARGO",
            "RAMP_MIL_COMBAT",
            "VEHICLE",
        };

        static String[] strTaxiwayPathTypes = new String[]  {
            "RUNWAY",
            "PARKING",
            "TAXI",
            "PATH",
            "CLOSED",
            "VEHICLE",
        };

            
        static public void GetSceneryObjects()
        {
            String strPath = Path.GetDirectoryName(Program.Config.FSXPath);
            String strBGL2XMLPath = @"C:\Programme\Microsoft Games\Microsoft Flight Simulator X SDK\Tools\BGL2XML_CMD\Bgl2Xml.exe";
            strPath += "\\Scenery";
            String strTmpFile = Path.GetTempFileName();
            String[] strFiles = Directory.GetFiles(strPath, "*.bgl", SearchOption.AllDirectories);

            String strName = "";
            String strIdent = "";
            String strRegion = "";
            float fLon = 0.0f;
            float fLat = 0.0f;
            float fFreq = 0.0f;
            float fMagVar = 0.0f;
            float fAlt = 0.0f;
            float fRange = 0.0f;

            int nFileNr = 1;

            OleDbConnection dbCon = new OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + Program.Config.AppPath + "\\data\\fsxget.mdb");
            dbCon.Open();
            OleDbCommand cmd = new OleDbCommand("DELETE * FROM navaids", dbCon);
//            cmd.ExecuteNonQuery();

            foreach (String strBGLFile in strFiles)
            {
                String strHead = Path.GetFileName(strBGLFile).Substring(0, 3).ToUpper();
                if(!( strHead == "NVX" || strHead == "APX"))
                {
                    continue;
                }
                System.Diagnostics.Trace.WriteLine(strBGLFile);
                try
                {
                    System.Diagnostics.ProcessStartInfo ps = new System.Diagnostics.ProcessStartInfo(strBGL2XMLPath, "\"" + strBGLFile + "\" \"" + strTmpFile + "\"");
                    ps.CreateNoWindow = true;
                    ps.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    System.Diagnostics.Process p = System.Diagnostics.Process.Start(ps);
                    int nSecs = 0;
                    while (!p.HasExited && nSecs < 600)
                    {
                        nSecs++;
                        Thread.Sleep(1000);
                    }
                    if (!p.HasExited)
                    {
                        System.Diagnostics.Trace.WriteLine("Killed");
                        p.Kill();
                        continue;
                    }
                }
                catch
                {
                }

//                try
                {
                    XmlDocument xmld = new XmlDocument();
                    xmld.Load(strTmpFile);

                    XmlNodeList nodes;
                    nodes = xmld.GetElementsByTagName("Vor");
                    foreach (XmlNode xmln in nodes)
                    {
                        bool bDme = false;
                        bool bDmeOnly = false;
                        foreach (XmlAttribute xmla in xmln.Attributes)
                        {
                            if (xmla.Name == "dme")
                                bDme = xmla.Value.ToLower() == "true";
                            else if (xmla.Name == "dmeOnly")
                                bDmeOnly = xmla.Value.ToLower() == "true";
                            else if (xmla.Name == "lat")
                                fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "lon")
                                fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "alt")
                            {
                                fAlt = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                            }
                            else if (xmla.Name == "range")
                                fRange = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "frequency")
                                fFreq = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "magvar")
                            {
                                fMagVar = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            }
                            else if (xmla.Name == "ident")
                            {
                                strIdent = xmla.Value;
                            }
                            else if (xmla.Name == "name")
                                strName = xmla.Value;
                            else if (xmla.Name == "region")
                                strRegion = xmla.Value;
                        }
                        int nType;
                        if (bDmeOnly)
                        {
                            nType = 1;      // Only DME
                        }
                        else
                        {
                            if (bDme)
                            {
                                nType = 3;  // VOR / DME
                            }
                            else
                            {
                                nType = 2;  // VOR
                            }
                        }
                        cmd.CommandText = "INSERT INTO navaids ( Ident, Name, TypeID, Longitude, Latitude, Altitude, MagVar, Range, Freq ) VALUES ( '" +
                            strIdent + "', '" +
                            strName.Replace("'", "''") + "', " +
                            nType.ToString() + ", " +
                            fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fAlt.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fMagVar.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fRange.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fFreq.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + ");";
                        cmd.ExecuteNonQuery();
                    }
                    nodes = xmld.GetElementsByTagName("Ndb");
                    foreach (XmlNode xmln in nodes)
                    {
                        foreach (XmlAttribute xmla in xmln.Attributes)
                        {
                            if (xmla.Name == "lat")
                                fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "lon")
                                fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "alt")
                            {
                                fAlt = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                            }
                            else if (xmla.Name == "range")
                                fRange = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "frequency")
                                fFreq = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "magvar")
                                fMagVar = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "ident")
                            {
                                strIdent = xmla.Value;
                            }
                            else if (xmla.Name == "name")
                                strName = xmla.Value;
                        }
                        cmd.CommandText = "INSERT INTO navaids ( Ident, Name, TypeID, Longitude, Latitude, Altitude, MagVar, Range, Freq ) VALUES ( '" +
                            strIdent + "', '" +
                            strName.Replace("'", "''") + "', 4," +
                            fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fAlt.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fMagVar.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fRange.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fFreq.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + ");";
                        cmd.ExecuteNonQuery();
                    }
                    
                    nodes = xmld.GetElementsByTagName("Airport");
                    foreach (XmlNode xmln in nodes)
                    {
                        int nBoundNr = 0;
                        foreach (XmlAttribute xmla in xmln.Attributes)
                        {
                            if (xmla.Name == "lat")
                                fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "lon")
                                fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "alt")
                                fAlt = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "magvar")
                                fMagVar = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                            else if (xmla.Name == "ident")
                            {
                                strIdent = xmla.Value;
                                if (strIdent == "EDDF")
                                    File.Copy(strTmpFile, "C:\\EDDF.xml");
                            }
                            else if (xmla.Name == "name")
                                strName = xmla.Value;
                        }

                        cmd.CommandText = "INSERT INTO airports ( Ident, Name, Longitude, Latitude, Altitude, MagVar ) VALUES ( '" +
                            strIdent + "', '" +
                            strName.Replace("'", "''") + "'," +
                            fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fAlt.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                            fMagVar.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + ");";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "SELECT @@IDENTITY";
                        int nAPID = (int)cmd.ExecuteScalar();

                        for (XmlNode xmlnChild = xmln.FirstChild; xmlnChild != null; xmlnChild = xmlnChild.NextSibling)
                        {
                            int nType = 0;
                            float fHeading = 0;
                            float fLength = 0;
                            float fWidth = 0;
                            int nNumber = 0;
                            char cPrimDesignator = 'N';
                            char cSekDesignator = 'N';
                            float fPatAlt = 0;
                            bool bPrimPatternRight = false;
                            bool bSekPatternRight = false;
                            int nIdx = 0;
                            int nName = 0;

                            if (xmlnChild.Name == "Com")
                            {
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "frequency")
                                        fFreq = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "type")
                                    {
                                        foreach (String strType in strComTypes)
                                        {
                                            nType++;
                                            if (strType == xmla.Value)
                                                break;
                                        }
                                        if (nType > strComTypes.Length)
                                            throw new Exception("Invalid ComType");
                                    }
                                    else if (xmla.Name == "name")
                                        strName = xmla.Value;
                                }
                                cmd.CommandText = "INSERT INTO AirportComs (AirportID, Name, Freq, TypeID) VALUES (" +
                                    nAPID.ToString() + "," +
                                    "'" + strName.Replace( "'", "''" ) + "'," +
                                    fFreq.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    nType.ToString() + ");";
                                cmd.ExecuteNonQuery();
                            }
                            else if (xmlnChild.Name == "Runway")
                            {
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "lat")
                                        fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "lon")
                                        fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "alt")
                                        fAlt = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "surface")
                                    {
                                        foreach (String strSurface in strSurfaces)
                                        {
                                            nType++;
                                            if (strSurface == xmla.Value)
                                                break;
                                        }
                                        if (nType > strSurfaces.Length)
                                            throw new Exception("Invalid SurfaceType");
                                    }
                                    else if (xmla.Name == "heading")
                                        fHeading = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "length")
                                        fLength = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "width")
                                    {
                                        fWidth = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                    }
                                    else if (xmla.Name == "number")
                                    {
                                        if (xmla.Value == "EAST")
                                            nNumber = 1090;
                                        else if (xmla.Value == "NORTH")
                                            nNumber = 1000;
                                        else if (xmla.Value == "NORTHEAST")
                                            nNumber = 1045;
                                        else if (xmla.Value == "NORTHWEST")
                                            nNumber = 1315;
                                        else if (xmla.Value == "SOUTH")
                                            nNumber = 1180;
                                        else if (xmla.Value == "SOUTHEAST")
                                            nNumber = 1135;
                                        else if (xmla.Value == "SOUTHWEST")
                                            nNumber = 1225;
                                        else if (xmla.Value == "WEST")
                                            nNumber = 1270;
                                        else
                                            nNumber = int.Parse(xmla.Value);
                                    }
                                    else if (xmla.Name == "designator")
                                    {
                                        cPrimDesignator = xmla.Value[0];
                                        if (cPrimDesignator == 'L')
                                            cSekDesignator = 'R';
                                        else if (cPrimDesignator == 'R')
                                            cSekDesignator = 'L';
                                        else
                                            cSekDesignator = xmla.Value[0];
                                    }
                                    else if (xmla.Name == "primaryDesignator")
                                        cPrimDesignator = xmla.Value[0];
                                    else if (xmla.Name == "secondaryDesignator")
                                        cSekDesignator = xmla.Value[0];
                                    else if (xmla.Name == "patternAltitude")
                                        fPatAlt = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "primaryPattern")
                                        bPrimPatternRight = xmla.Value == "RIGHT";
                                    else if (xmla.Name == "secondaryPattern")
                                        bSekPatternRight = xmla.Value == "RIGHT";
                                }
                                cmd.CommandText = "INSERT INTO Runways (AirportID, Longitude, Latitude, Altitude, Heading, Length, Width, [Number], SurfaceID, PrimaryDesignator, SecondaryDesignator, PatternAltitude, PrimaryPatternRight, SecondaryPatternRight) VALUES (" +
                                    nAPID.ToString() + "," +
                                    fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fAlt.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fHeading.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fLength.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fWidth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    nNumber.ToString() + "," +
                                    nType.ToString() + "," +
                                    "'" + cPrimDesignator + "'," +
                                    "'" + cSekDesignator + "'," +
                                    fPatAlt.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," + 
                                    (bPrimPatternRight ? "1" : "0") + "," +
                                    (bSekPatternRight ? "1" : "0") + ");";
                                cmd.ExecuteNonQuery();
                                cmd.CommandText = "SELECT @@IDENTITY";
                                int nRunwayID = (int)cmd.ExecuteScalar();
                                bool bHasLights = false;
                                for (XmlNode xmlnRWChild = xmlnChild.FirstChild; xmlnRWChild != null; xmlnRWChild = xmlnRWChild.NextSibling)
                                {
                                    bool bEndSec = false;
                                    bool bBackCourse = false;
                                    if (xmlnRWChild.Name == "Ils")
                                    {
                                        foreach (XmlAttribute xmla in xmlnRWChild.Attributes)
                                        {
                                            if (xmla.Name == "lat")
                                                fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "lon")
                                                fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "alt")
                                                fAlt = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "heading")
                                                fHeading = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "frequency")
                                                fFreq = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "frequency")
                                                fFreq = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "end")
                                                bEndSec = xmla.Value == "SECONDARY";
                                            else if (xmla.Name == "range")
                                                fRange = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "magvar")
                                                fMagVar = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "ident")
                                                strIdent = xmla.Value;
                                            else if (xmla.Name == "width")
                                                fWidth = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                            else if (xmla.Name == "name")
                                                strName = xmla.Value;
                                            else if (xmla.Name == "backCourse")
                                                bBackCourse = xmla.Value == "TRUE";
                                        }
                                        cmd.CommandText = "INSERT INTO RunwayILS (RunwayID, Name, Longitude, Latitude, Altitude, Freq, EndSecondary, Range, MagVar, Ident, Width, Heading, BackCourse) VALUES (" +
                                            nRunwayID.ToString() + "," +
                                            "'" + strName.Replace("'", "''") + "'," +
                                            fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            fAlt.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            fFreq.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            (bEndSec ? "1" : "0") + "," +
                                            fRange.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            fMagVar.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            "'" + strIdent + "'," +
                                            fWidth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            fHeading.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                            (bBackCourse ? "1" : "0") + ");";
                                        cmd.ExecuteNonQuery();    
                                    }
                                    else if (xmlnRWChild.Name == "Lights")
                                    {
                                        XmlAttribute xmla = xmlnRWChild.Attributes["center"];
                                        if (xmla != null)
                                            bHasLights = xmla.Value != "NONE";
                                        if (!bHasLights)
                                        {
                                            xmla = xmlnRWChild.Attributes["edge"];
                                            if (xmla != null)
                                                bHasLights = xmla.Value != "NONE";
                                        }
                                        if (bHasLights)
                                        {
                                            cmd.CommandText = "UPDATE Runways SET HasLights=1 WHERE ID=" + nRunwayID.ToString();
                                            cmd.ExecuteNonQuery();
                                        }
                                        
                                    }
                                }
                            }
                            else if (xmlnChild.Name == "TaxiwayPoint")
                            {
                                bool bReverse = false;
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "index")
                                        nIdx = int.Parse(xmla.Value);
                                    else if (xmla.Name == "type")
                                    {
                                        foreach (String strType in strTaxiPointTypes)
                                        {
                                            nType++;
                                            if (strType == xmla.Value)
                                                break;
                                        }
                                        if (nType > strTaxiPointTypes.Length)
                                            throw new Exception("Invalid TaxiwayPointType");
                                    }
                                    else if (xmla.Name == "orientation")
                                        bReverse = xmla.Value == "REVERSE";
                                    else if (xmla.Name == "lat")
                                        fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "lon")
                                        fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                }
                                cmd.CommandText = "INSERT INTO TaxiwayPoints (AirportID, [Index], TypeID, Longitude, Latitude, [Reverse]) VALUES (" +
                                    nAPID.ToString() + "," +
                                    nIdx.ToString() + "," +
                                    nType.ToString() + "," +
                                    fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    (bReverse ? "1" : "0") + ");";
                                cmd.ExecuteNonQuery();
                            }
                            else if (xmlnChild.Name == "TaxiwayParking")
                            {
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "index")
                                        nIdx = int.Parse(xmla.Value);
                                    else if (xmla.Name == "lat")
                                        fLat = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "lon")
                                        fLon = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "heading")
                                        fHeading = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "radius")
                                        fRange = float.Parse(xmla.Value.Substring( 0, xmla.Value.Length-1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "type")
                                    {
                                        foreach (String strType in strTaxiwayParkingTypes)
                                        {
                                            nType++;
                                            if (strType == xmla.Value)
                                                break;
                                        }
                                        if (nType > strTaxiwayParkingTypes.Length)
                                            throw new Exception("Invalid TaxiwayParkingType");
                                    }
                                    else if (xmla.Name == "name")
                                    {
                                        foreach (String str in strTaxiwayParkingNames)
                                        {
                                            nName++;
                                            if (str == xmla.Value)
                                                break;
                                        }
                                        if (nName > strTaxiwayParkingNames.Length)
                                            throw new Exception("Invalid TaxiwayParkingName");
                                    }
                                    else if (xmla.Name == "number")
                                        nNumber = int.Parse(xmla.Value);
                                }
                                cmd.CommandText = "INSERT INTO TaxiwayParking (AirportID, [Index], Longitude, Latitude, Heading, Radius, TypeID, NameID, [Number]) VALUES (" +
                                    nAPID.ToString() + "," +
                                    nIdx.ToString() + "," +
                                    fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fHeading.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fRange.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    nType.ToString() + "," +
                                    nName.ToString() + "," +
                                    nNumber.ToString() + ");";
                                cmd.ExecuteNonQuery();
                            }
                            else if (xmlnChild.Name == "TaxiwayPath")
                            {
                                int nSurface = 0;
                                int nIdxEnd = 0;
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "type")
                                    {
                                        foreach (String str in strTaxiwayPathTypes)
                                        {
                                            nType++;
                                            if (str == xmla.Value)
                                                break;
                                        }
                                        if (nType > strTaxiwayPathTypes.Length)
                                            throw new Exception("Invalid TaxiwayPathType");
                                    }
                                    else if (xmla.Name == "start")
                                        nIdx = int.Parse(xmla.Value);
                                    else if (xmla.Name == "end")
                                        nIdxEnd = int.Parse(xmla.Value);
                                    else if (xmla.Name == "width")
                                        fWidth = float.Parse(xmla.Value.Substring(0, xmla.Value.Length - 1), System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "surface")
                                    {
                                        foreach (String str in strSurfaces)
                                        {
                                            nSurface++;
                                            if (str == xmla.Value)
                                                break;
                                        }
                                        if (nSurface > strSurfaces.Length)
                                            throw new Exception("Invalid SurfaceType");
                                    }
                                    else if (xmla.Name == "number")
                                    {
                                        if (xmla.Value == "EAST")
                                            nNumber = 1090;
                                        else if (xmla.Value == "NORTH")
                                            nNumber = 1000;
                                        else if (xmla.Value == "NORTHEAST")
                                            nNumber = 1045;
                                        else if (xmla.Value == "NORTHWEST")
                                            nNumber = 1315;
                                        else if (xmla.Value == "SOUTH")
                                            nNumber = 1180;
                                        else if (xmla.Value == "SOUTHEAST")
                                            nNumber = 1135;
                                        else if (xmla.Value == "SOUTHWEST")
                                            nNumber = 1225;
                                        else if (xmla.Value == "WEST")
                                            nNumber = 1270;
                                        else
                                            nNumber = int.Parse(xmla.Value);
                                    }
                                    else if (xmla.Name == "designator")
                                        cPrimDesignator = xmla.Value[0];
                                    else if (xmla.Name == "name")
                                        nName = int.Parse(xmla.Value);
                                }
                                cmd.CommandText = "INSERT INTO TaxiwayPaths (AirportID, StartPointIndex, EndPointIndex, NameIndex, TypeID, Width, SurfaceID, [Number], Designator) VALUES (" +
                                    nAPID.ToString() + "," +
                                    nIdx.ToString() + "," +
                                    nIdxEnd.ToString() + "," +
                                    nName.ToString() + "," +
                                    nType.ToString() + "," +
                                    fWidth.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    nSurface.ToString() + "," +
                                    nNumber.ToString() + "," +
                                    "'" + cPrimDesignator + "');";
                                cmd.ExecuteNonQuery();
                            }
                            else if (xmlnChild.Name == "TaxiName")
                            {
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "index")
                                        nIdx = int.Parse(xmla.Value);
                                    else if (xmla.Name == "name")
                                        strName = xmla.Value;
                                }
                                cmd.CommandText = "INSERT INTO TaxiNames (AirportID, [Index], Name) VALUES (" +
                                    nAPID.ToString() + "," +
                                    nIdx.ToString() + "," +
                                    "'" + strName.Replace("'", "''") + "');";
                                cmd.ExecuteNonQuery();
                            }
                            else if (xmlnChild.Name == "TaxiwaySign")
                            {
                                foreach (XmlAttribute xmla in xmlnChild.Attributes)
                                {
                                    if (xmla.Name == "lat")
                                        fLat = FsxConnection.ConvertDegToFloat2(xmla.Value);//float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "lon")
                                        fLon = FsxConnection.ConvertDegToFloat2(xmla.Value); //float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "heading")
                                        fHeading = float.Parse(xmla.Value, System.Globalization.NumberFormatInfo.InvariantInfo);
                                    else if (xmla.Name == "label")
                                        strName = xmla.Value;
                                    else if (xmla.Name == "size")
                                        nIdx = xmla.Value[4] - '0';
                                    else if (xmla.Name == "justification")
                                        bPrimPatternRight = xmla.Value == "RIGHT";
                                }
                                cmd.CommandText = "INSERT INTO TaxiwaySigns (AirportID, Longitude, Latitude, Heading, Label, JustifyRight, [Size]) VALUES (" +
                                    nAPID.ToString() + "," +
                                    fLon.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fLat.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    fHeading.ToString(System.Globalization.NumberFormatInfo.InvariantInfo) + "," +
                                    "'" + strName.Replace("'", "''") + "'," +
                                    (bPrimPatternRight ? "1" : "0") + "," +
                                    nIdx.ToString() + ");";
                                cmd.ExecuteNonQuery();
                            }
                            else if (xmlnChild.Name == "BoundaryFence")
                            {
                                nIdx = 0;
                                cmd.CommandText = "INSERT INTO AirportBoundary (AirportID, [Number]) VALUES (" +
                                    nAPID.ToString() + "," +
                                    nBoundNr.ToString() + ");";
                                cmd.ExecuteNonQuery();
                                cmd.CommandText = "SELECT @@IDENTITY";
                                int nBoundID = (int)cmd.ExecuteScalar();
                                for (XmlNode xmlnVertex = xmlnChild.FirstChild; xmlnVertex != null; xmlnVertex = xmlnVertex.NextSibling)
                                {
                                    cmd.CommandText = "INSERT INTO AirportBoundaryVertex (BoundaryID, SortNr, Longitude, Latitude) VALUES (" +
                                        nBoundID.ToString() + "," +
                                        nIdx.ToString() + "," +
                                        xmlnVertex.Attributes["lon"].Value + "," +
                                        xmlnVertex.Attributes["lat"].Value + ");";
                                    cmd.ExecuteNonQuery();
                                    nIdx++;
                                }
                                nBoundNr++;
                            }
                        }
                    }

                    xmld = null;
                }
//                catch( Exception e )
                {
//                    System.Diagnostics.Trace.WriteLine(e.Message);
                }
            }
            dbCon.Close();
            File.Delete(strTmpFile);
        }
        #endregion
    }
}
