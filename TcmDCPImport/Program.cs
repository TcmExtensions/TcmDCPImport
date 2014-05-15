using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcmDCPImport
{
	public class ComponentPresentationInfo
	{
		public int Publication
		{
			get;
			set;
		}

		public int Component
		{
			get;
			set;
		}

		public int Template
		{
			get;
			set;
		}

		public String OutputFormat
		{
			get;
			set;
		}

		public String Extension
		{
			get
			{
				switch (OutputFormat)
				{
					case "Plain Text":
						return "txt";
					case "XML Document":
						return "xml";
					case "XML Fragment":
						return "xml";
					case "HTML":
						return String.Empty;
					default:
						return String.Empty;
				}
			}
		}

		public String Filename
		{
			get
			{
				return String.Format("dcp{0}_{1}.{2}", Template, Component, Extension);
			}
		}

		public String Path
		{
			get
			{
				return String.Format(@"pub{0}\dcp\{1}\{2}", Publication, Extension, Filename);
			}
		}
	}

	class Program
	{
		private const String QUERY_DB_INFORMATION = "SELECT DB_VERSION, DESCRIPTION FROM TDS_DB_INFO;";

		private const String QUERY_COMPONENT_PRESENTATIONS = "SELECT CPMD.PUBLICATION_ID, CPMD.COMPONENT_REF_ID, CPMD.COMPONENT_TEMPLATE_ID, CPMD.COMPONENT_OUTPUT_FORMAT " +
															 "FROM COMPONENT_PRES_META_DATA AS CPMD " +
															 "LEFT JOIN COMPONENT_PRESENTATIONS AS CP " +
															 "ON CPMD.PUBLICATION_ID = CP.PUBLICATION_ID " +
															 "AND CPMD.COMPONENT_TEMPLATE_ID = CP.TEMPLATE_ID " +
															 "AND CPMD.COMPONENT_REF_ID = CP.COMPONENT_ID " +
															 "WHERE CP.COMPONENT_ID IS NULL;";

		private const String INSERT_COMPONENT_PRESENTATION = "INSERT INTO COMPONENT_PRESENTATIONS VALUES (@publication, @component, @template, @content);";

		private static readonly Object mLock = new Object();
		private static FileStream mLogStream;
		private static StreamWriter mLog;

		private static String ApplicationPath
		{
			get
			{
				return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
			}
		}

		private static void WriteLog(String message, params Object[] args)
		{
			lock (mLock)
			{
#if DEBUG
				Console.WriteLine(message, args);
#endif

				mLog.WriteLine(message, args);
				mLog.Flush();
				mLogStream.Flush(true);
			}
		}

		private static bool ParseArguments(String[] args, out String connectionString, out String path)
		{
			String server, username, password, database;

			connectionString = String.Empty;
			path = String.Empty;

			if (args.Length != 5)
			{
				Console.Write("[.] Server: ");
				server = Console.ReadLine();

				Console.Write("[.] Username: ");
				username = Console.ReadLine();

				Console.Write("[.] Password: ");
				password = Console.ReadLine();

				Console.Write("[.] Database: ");
				database = Console.ReadLine();

				Console.Write("[.] DCP Path: ");
				path = Console.ReadLine();
			}
			else
			{
				server = args[0];
				username = args[1];
				password = args[2];
				database = args[3];
				path = args[4];
			}

			Console.WriteLine();
			Console.WriteLine("[i] Connecting to:");
			Console.WriteLine("    Server:\t{0}", server);
			Console.WriteLine("    Username:\t{0}", username);
			Console.WriteLine("    Password:\t{0}", password);
			Console.WriteLine("    Database:\t{0}", database);
			Console.WriteLine("    DCP Path:\t{0}", path);
			Console.WriteLine();
			Console.WriteLine("Press enter to confirm, CTRL+C to cancel");
			Console.ReadLine();

			if (!Directory.Exists(path))
			{
				Console.WriteLine("[!] DCP Path \"{0}\" could not be found", path);
				return false;
			}

			SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder()
			{
				DataSource = server,
				UserID = username,
				Password = password,
				InitialCatalog = database,
				MultipleActiveResultSets = true
			};

			connectionString = builder.ConnectionString;

			return true;
		}

		private static bool GetDBInfo(SqlConnection connection)
		{
			try
			{
				Console.WriteLine("[i] Connecting to \"{0}\", Database \"{1}\".", connection.DataSource, connection.Database);

				using (SqlCommand command = new SqlCommand(QUERY_DB_INFORMATION, connection))
				{
					using (SqlDataReader reader = command.ExecuteReader())
					{
						if (reader.HasRows)
						{
							reader.Read();
							Console.WriteLine("[i] Connected to \"{0}\", Version \"{1}\".", reader.GetString(1), reader.GetString(0));
						}

						return true;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("[!] {0}", ex.Message);
			}

			return false;
		}

		private static IEnumerable<ComponentPresentationInfo> ReadComponentPresentations(SqlConnection connection)
		{
			using (SqlCommand command = new SqlCommand(QUERY_COMPONENT_PRESENTATIONS, connection))
			{
				SqlDataReader reader = command.ExecuteReader();

				if (reader.HasRows)
				{
					while (reader.Read())
					{
						yield return new ComponentPresentationInfo()
						{
							Publication = reader.GetInt32(0),
							Component = reader.GetInt32(1),
							Template = reader.GetInt32(2),
							OutputFormat = reader.GetString(3)
						};
					}
				}
			}
		}

		static void Main(String[] args)
		{
			using (mLogStream = new FileStream(Path.Combine(ApplicationPath, "TcmDCPImport.log"), FileMode.Append, FileAccess.Write, FileShare.Read))
			{
				using (mLog = new StreamWriter(mLogStream))
				{
					Console.WriteLine("[i] Tridion Dynamic Component Presentation Importer");

					String connectionString;
					String path;

					if (!ParseArguments(args, out connectionString, out path))
						return;

					using (SqlConnection connection = new SqlConnection(connectionString))
					{
						connection.Open();

						if (!GetDBInfo(connection))
						{
							Console.WriteLine("[!] Unable to connect to database");
							return;
						}

						Parallel.ForEach(ReadComponentPresentations(connection), new ParallelOptions()
						{
							MaxDegreeOfParallelism = 8
						},
						(componentPresentation) =>
						{
							String relativePath = componentPresentation.Path;

							try
							{
								String filePath = Path.Combine(path, relativePath);

								if (!File.Exists(filePath))
								{
									WriteLog("{0} not found.", relativePath);
									return;
								}

								String fileContents = File.ReadAllText(filePath, Encoding.UTF8);
								String backupPath = Path.Combine(path, "backup", relativePath);

								try
								{
									// Move the file into the backup folder									
									Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
									File.Move(filePath, backupPath);
								}
								catch (Exception ex)
								{
									WriteLog("{0} error archiving {1}.", relativePath, ex.Message);
									return;
								}

								using (SqlCommand command = new SqlCommand(INSERT_COMPONENT_PRESENTATION, connection))
								{
									command.Parameters.AddRange(
										new SqlParameter[]
									{
										new SqlParameter("publication", SqlDbType.Int)
										{
											Direction = ParameterDirection.Input,
											Value = componentPresentation.Publication
										},
										new SqlParameter("component", SqlDbType.Int)
										{
											Direction = ParameterDirection.Input,
											Value = componentPresentation.Component
										},
										new SqlParameter("template", SqlDbType.Int)
										{
											Direction = ParameterDirection.Input,
											Value = componentPresentation.Template
										},
										new SqlParameter("content", SqlDbType.NVarChar)
										{
											Direction = ParameterDirection.Input,
											Value = fileContents
										}
									});

									try
									{
										if (command.ExecuteNonQuery() != 1)
											throw new Exception("No result rows");
									}
									catch (Exception ex)
									{
										WriteLog("{0} error inserting into database: {1}", relativePath, ex.Message);

										// Query failed, move file back into source
										File.Move(backupPath, filePath);
										return;
									}
								}

								WriteLog("{0} imported.", relativePath);
							}
							catch (Exception ex)
							{
								WriteLog("{0} error {1}.", relativePath, ex.Message);
							}
						});
					}					
				}
			}

			Console.ReadKey();
		}
	}
}