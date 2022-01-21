﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AS = Microsoft.AnalysisServices;
using Microsoft.AnalysisServices.Tabular;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Dynamic;
using System.Windows.Controls;
using System.Windows.Data;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.VisualBasic.FileIO;
using Adomd = Microsoft.AnalysisServices.AdomdClient;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Resources;
using System.Collections;

namespace Metadata_Translator
{
    public class DataModel
    {
        Model Model { get; set; }

        public string ContainerColumnHeader { get => "Object"; }
        public ObservableCollection<ExpandoObject> Captions { get; private set; }
        public ObservableCollection<ExpandoObject> Descriptions { get; private set; }
        public ObservableCollection<ExpandoObject> DisplayFolders { get; private set; }

        public string DefaultCulture { get; set; }

        public string ServerName { get; private set; }
        public string DatabaseName { get; private set; }

        public List<string> CultureNames 
        {
            get
            {
                List<string> cultures = new List<string> { Model.Culture };
                cultures.AddRange(Model?.Cultures.Where(i => !i.Name.Equals(Model.Culture)).Select(x => x.Name).ToList());
                return cultures;
            }
        }
        public List<Language> SupportedLanguages { get; private set; }
        public List<Language> SelectedLanguages { get => SupportedLanguages?.Where(x => x.IsSelected==true).ToList(); }
        public bool HasTargetLanguages { get => SelectedLanguages?.Count > 1; }
        
        private const string appTag = "__MT";

        /// <summary>
        /// Connect to the dataset by using server and database name. This is how external tools typically connect to a dataset inside of Power BI Desktop.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="database"></param>
        public DataModel(string server, string database)
        {
            ServerName = server;
            DatabaseName = database;

            Server pbiDesktop = new Server();
            pbiDesktop.Connect($"Data Source={ServerName}");
            Database dataset = pbiDesktop.Databases.GetByName(DatabaseName);
            Model = dataset.Model;

            Initialize();
        }

        /// <summary>
        /// Connects to a dataset using a connection string. This is how  tools typically connect to online datasets in SQL Server Analysis Services, Azure Analysis Services, and Power BI.
        /// </summary>
        /// <param name="connectionString"></param>
        public DataModel(string connectionString)
        {
            /// Connect using the full connection string, as it may contain more than
            /// just data source and intial catalog, such as user id and password.
            /// 
            Server pbiDesktop = new Server();
            pbiDesktop.Connect(connectionString);

            /// Parse the connection string using regex to avoid resolving server and database names through the AMO objects.
            /// 
            RegexOptions options = RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled;
            foreach (Match match in Regex.Matches(connectionString, "([^=;]*)=([^=;]*)", options))
            {
                string[] parts = match.Value.Split('=');
                if (parts?.Length == 2 && parts[0].ToLower() == "data source")
                {
                    ServerName = parts[1];
                }
                else if (parts?.Length == 2 && parts[0].ToLower() == "initial catalog")
                {
                    DatabaseName = parts[1];
                }
            }

            /// Select the database based on the extracted database name.
            /// 
            Database dataset = pbiDesktop.Databases.GetByName(DatabaseName);
            Model = dataset.Model;

            Initialize();
        }

        /// <summary>
        /// Initializes the list of supported languages and the named object collections.
        /// </summary>
        private void Initialize()
        {
            LoadLanguages();
            DefaultCulture = Model.Culture;
            LoadNamedObjectCollections();
        }

        /// <summary>
        /// A static helper to get the DataModel object based on server and database name.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static DataModel Connect(string server, string database)
        {
            return new DataModel(server, database);
        }

        /// <summary>
        /// A static helper to get the DataModel object based on a connection string.
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static DataModel Connect(string connectionString)
        {
            return new DataModel(connectionString);
        }

        /// <summary>
        /// Gets the tables from the dataset and within it all the columns, measures, and hierarchies
        /// and adds these tabular objects to the collections for captions, descriptions, and display folders.
        /// </summary>
        private void LoadNamedObjectCollections()
        {
            Captions = new ObservableCollection<ExpandoObject>();
            Descriptions = new ObservableCollection<ExpandoObject>();
            DisplayFolders = new ObservableCollection<ExpandoObject>();

            CultureCollection cultures = Model.Cultures;

            Captions.Add(CreateRow(new MetadataObjectContainer(Model, TranslatedProperty.Caption), Model.Name, DefaultCulture, cultures));
            if (!string.IsNullOrEmpty(Model.Description))
                Descriptions.Add(CreateRow(new MetadataObjectContainer(Model, TranslatedProperty.Description), Model.Description, DefaultCulture, cultures));

            foreach (Table table in Model.Tables)
            {
                Captions.Add(CreateRow(new MetadataObjectContainer(table, TranslatedProperty.Caption), table.Name, DefaultCulture, cultures));
                if (!string.IsNullOrEmpty(table.Description))
                    Descriptions.Add(CreateRow(new MetadataObjectContainer(table, TranslatedProperty.Description), table.Description, DefaultCulture, cultures));
                foreach (Column column in table.Columns)
                {
                    if (column.Type != ColumnType.RowNumber)
                    {
                        Captions.Add(CreateRow(new MetadataObjectContainer(column, TranslatedProperty.Caption), column.Name, DefaultCulture, cultures));

                        if (!string.IsNullOrEmpty(column.Description))
                            Descriptions.Add(CreateRow(new MetadataObjectContainer(column, TranslatedProperty.Description), column.Description, DefaultCulture, cultures));
                        if (!string.IsNullOrEmpty(column.DisplayFolder))
                            DisplayFolders.AddDisplayFolder(column, column.DisplayFolder, DefaultCulture, cultures);
                    }
                }

                foreach (Measure measure in table.Measures)
                {
                    Captions.Add(CreateRow(new MetadataObjectContainer(measure, TranslatedProperty.Caption), measure.Name, DefaultCulture, cultures));

                    if (!string.IsNullOrEmpty(measure.Description))
                        Descriptions.Add(CreateRow(new MetadataObjectContainer(measure, TranslatedProperty.Description), measure.Description, DefaultCulture, cultures));
                    if (!string.IsNullOrEmpty(measure.DisplayFolder))
                        DisplayFolders.AddDisplayFolder(measure, measure.DisplayFolder, DefaultCulture, cultures);
                }

                foreach (Hierarchy hierarchy in table.Hierarchies)
                {
                    Captions.Add(CreateRow(new MetadataObjectContainer(hierarchy, TranslatedProperty.Caption), hierarchy.Name, DefaultCulture, cultures));

                    if (!string.IsNullOrEmpty(hierarchy.Description))
                        Descriptions.Add(CreateRow(new MetadataObjectContainer(hierarchy, TranslatedProperty.Description), hierarchy.Description, DefaultCulture, cultures));
                    if (!string.IsNullOrEmpty(hierarchy.DisplayFolder))
                        DisplayFolders.AddDisplayFolder(hierarchy, hierarchy.DisplayFolder, DefaultCulture, cultures);

                    foreach(Level level in hierarchy.Levels)
                    {
                        Captions.Add(CreateRow(new MetadataObjectContainer(level, TranslatedProperty.Caption), level.Name, DefaultCulture, cultures));

                        if (!string.IsNullOrEmpty(level.Description))
                            Descriptions.Add(CreateRow(new MetadataObjectContainer(level, TranslatedProperty.Description), level.Description, DefaultCulture, cultures));
                    }
                }
            }
        }

        /// <summary>
        /// Loads the list of supported languages from the supportedlanguages.json file.
        /// </summary>
        private void LoadLanguages()
        {
            SupportedLanguages = new List<Language>();
            string content = File.ReadAllText($"{System.AppDomain.CurrentDomain.BaseDirectory}Resources\\supportedlanguages.json");
            foreach (Language lang in new JavaScriptSerializer().Deserialize<List<Language>>(content))
            {
                SupportedLanguages.Add(lang);
            }
        }

        /// <summary>
        /// Marks the languages in the list of supported lanuages as selected and model default.
        /// </summary>
        public void InitializeLanguages()
        {
            if (this.CultureNames?.Count > 0)
            {
                /// First the model culture.
                /// 
                SetLanguageFlags(this.CultureNames[0], true, true);

                /// Then all others
                /// 
                for (int i = 1; i < this.CultureNames.Count; i++)
                {
                    SetLanguageFlags(this.CultureNames[i], true, false);
                }
            }
        }

        /// <summary>
        /// Marks all translation languages as unselected.
        /// </summary>
        public void DeselectAllLanguages()
        {
            foreach(Language lang in SelectedLanguages)
            {
                if (lang.IsNotModelDefault)
                    lang.IsSelected = false;
            }
        }

        /// <summary>
        /// Marks a language specified by lcid as selected and as model default.
        /// </summary>
        /// <param name="lcid"></param>
        /// <param name="isSelected"></param>
        /// <param name="isModelDefault"></param>
        public bool SetLanguageFlags(string lcid, bool isSelected, bool isModelDefault = false)
        {
            if (lcid.Equals(DefaultCulture, StringComparison.InvariantCultureIgnoreCase) && isModelDefault == false)
            {
                return false;
            }

            Language language = this.SupportedLanguages.Where(x => x.LanguageTag.Equals(lcid)).FirstOrDefault();
            if (language != null)
            {
                language.IsSelected = isSelected;
                language.IsModelDefault = isModelDefault;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the Language object based on the lcid (i.e. LanguageTag).
        /// </summary>
        /// <param name="lcid"></param>
        /// <returns></returns>
        public Language GetLanguageByLcid(string lcid)
        {
            return this.SupportedLanguages.Where(x => x.LanguageTag.Equals(lcid)).FirstOrDefault();
        }

        /// <summary>
        /// Creates a new ExpandoObject for a source string (displayString).
        /// </summary>
        /// <param name="objectContainer"></param>
        /// <param name="displayString"></param>
        /// <param name="defaultCulture"></param>
        /// <param name="cultures"></param>
        /// <returns>An ExpandoObject representing a data row.</returns>
        public ExpandoObject CreateRow(MetadataObjectContainer objectContainer, string displayString, string defaultCulture, CultureCollection cultures)
        {
            dynamic row = new ExpandoObject();

            ((IDictionary<String, Object>)row)[ContainerColumnHeader] = objectContainer;
            foreach (var culture in cultures)
            {
                ((IDictionary<String, Object>)row)[culture.Name] = culture.Name.Equals(defaultCulture) ? displayString :
                    culture.ObjectTranslations[objectContainer.TabularObject, objectContainer.TranslatedProperty]?.Value;
            }

            return row;
        }

        /// <summary>
        /// Combine all collections for translation and updating.
        /// </summary>
        /// <returns></returns>
        public List<ExpandoObject> GetAllDataRows()
        {
            var allRows = new List<ExpandoObject>();
            foreach (var item in Captions) allRows.Add(item);
            foreach (var item in Descriptions) allRows.Add(item);
            foreach (var item in DisplayFolders) allRows.Add(item);
            return allRows;
        }

        /// <summary>
        /// Adds a translation to a Tabular metadata object.
        /// </summary>
        /// <param name="culture"></param>
        /// <param name="metadataObjectContainer"></param>
        /// <param name="translation"></param>
        private void SetTranslation(Culture culture, MetadataObjectContainer metadataObjectContainer, string translation)
        {
            culture.ObjectTranslations.SetTranslation(
                    metadataObjectContainer.TabularObject, metadataObjectContainer.TranslatedProperty,
                    translation);
        }

        /// <summary>
        /// Updates the Power BI dataset with the translations from the ExpandoObject collections and saves the changes.
        /// </summary>
        public void Update()
        {
            /// Delete any deselected cultures that still exist in the dataset.
            ///
            List<string> cultureNames = SelectedLanguages?.Select(sl => sl.LanguageTag)?.ToList();

            /// There must be at least the default culture in the cultureNames.
            /// 
            if (cultureNames == null || cultureNames.Count < 1) return;

            var culturesToRemove = CultureNames.Where(cn1 => !cultureNames.Any(cn2 => cn2.Equals(cn1))).ToList();
            culturesToRemove.Remove(DefaultCulture);

            foreach(string cultureName in culturesToRemove)
            {
                if (Model.Cultures.Contains(cultureName))
                {
                    Model.Cultures.Remove(cultureName);
                }
            }

            /// Add any newly selected cultures.
            /// 
            foreach (string cultureName in cultureNames)
            {
                if (!Model.Cultures.Contains(cultureName))
                {
                    Model.Cultures.Add(new Culture { Name = cultureName });
                }
            }

            /// Add the translations to all the metadata objects.
            /// 
            foreach (ExpandoObject row in GetAllDataRows())
            {
                if (((IDictionary<string, Object>)row)[ContainerColumnHeader] is MetadataObjectContainer metadataObjectContainer)
                {
                    /*
                     * Include this part when updating the default culture (i.e. updating the actual metadata objects) is supported.
                     * 
                    switch (metadataObjectContainer.TranslatedProperty)
                    {
                        case TranslatedProperty.Caption:
                            metadataObjectContainer.TabularObject.Name = row.GetValue(DefaultCulture);
                            break;
                        case TranslatedProperty.Description:
                            if (metadataObjectContainer.TabularObject is Table table)
                            {
                                table.Description = row.GetValue(DefaultCulture);
                            }
                            else if (metadataObjectContainer.TabularObject is Column col)
                            {
                                col.Description = row.GetValue(DefaultCulture);
                            }
                            else if (metadataObjectContainer.TabularObject is Measure measure)
                            {
                                measure.Description = row.GetValue(DefaultCulture);
                            }
                            else if (metadataObjectContainer.TabularObject is Hierarchy hierarchy)
                            {
                                hierarchy.Description = row.GetValue(DefaultCulture);
                            }
                            break;
                        case TranslatedProperty.DisplayFolder:
                            if (metadataObjectContainer.TabularObject is Column column)
                            {
                                column.DisplayFolder = row.GetValue(DefaultCulture);
                            }
                            else if (metadataObjectContainer.TabularObject is Measure measure)
                            {
                                measure.DisplayFolder = row.GetValue(DefaultCulture);
                            }
                            else if (metadataObjectContainer.TabularObject is Hierarchy hierarchy)
                            {
                                hierarchy.DisplayFolder = row.GetValue(DefaultCulture);
                            }
                            break;
                    }
                    */
                    foreach (string cultureName in cultureNames)
                    {
                        SetTranslation(Model.Cultures[cultureName], 
                            metadataObjectContainer,
                            row.GetValue(cultureName));
                    }
                }
            }

            /// Save the changes in the dataset.
            /// 
            Annotation mtAnnotation = new Annotation();
            mtAnnotation.Name = appTag;
            mtAnnotation.Value = "1";
            if (!Model.Annotations.Contains(appTag))
            {
                Model.Annotations.Add(mtAnnotation);
            }

            Model.Database.Update(AS.UpdateOptions.ExpandFull);
        }

        /// <summary>
        /// Exports the translations to individual language (csv) files.
        /// The files are placed into the specified export folder.
        /// </summary>
        /// <param name="exportFolderPath"></param>
        public void ExportToCsv(string exportFolderPath)
        {
            string separator = ",";
            List<ExpandoObject> dataRows = GetAllDataRows();
            if (dataRows != null && dataRows.Count > 0)
            {
                List<string> languages = SelectedLanguages.Where(l => l.IsModelDefault != true).Select(l => l.LanguageTag).ToList();

                if (languages != null && languages.Count > 0)
                {
                    foreach (string lcid in languages)
                    {
                        StringBuilder csvContent = new StringBuilder();
                        csvContent.AppendLine("Type,Original,Translation");

                        foreach (var stringValues in dataRows.GetValues(ContainerColumnHeader, DefaultCulture, lcid))
                        {
                            csvContent.AppendLine(
                                string.Join(
                                    separator,
                                    new string[] {
                                                stringValues.Type.ToCsvString(),
                                                stringValues.Original.ToCsvString(),
                                                stringValues.Translation.ToCsvString()
                                    })
                                );
                        }

                        using (var sw = File.Create(System.IO.Path.Combine(exportFolderPath, $"{lcid}.csv")))
                        {
                            var preamble = Encoding.UTF8.GetPreamble();
                            sw.Write(preamble, 0, preamble.Length);
                            var data = Encoding.UTF8.GetBytes(csvContent.ToString());
                            sw.Write(data, 0, data.Length);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Exports the translations to individual resx files.
        /// The files are placed into the specified export folder.
        /// </summary>
        /// <param name="exportFolderPath"></param>
        public void ExportToResx(string exportFolderPath, string keyPrefix)
        {
            List<ExpandoObject> dataRows = GetAllDataRows();
            if (dataRows != null && dataRows.Count > 0)
            {
                List<string> languages = SelectedLanguages.Select(l => l.LanguageTag).ToList();

                if (languages != null && languages.Count > 0)
                {
                    foreach (string lcid in languages)
                    {
                        using (ResXResourceWriter resx = new ResXResourceWriter(System.IO.Path.Combine(exportFolderPath, $"{lcid}.resx")))
                        {
                            foreach(var kvp in dataRows.GetKeyValuePairs(ContainerColumnHeader, lcid, keyPrefix))
                            {
                                resx.AddResource(kvp.Key.ToString(), kvp.Value);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Imports translations from a csv file. The file name must match the LCID of the target language.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="lcid"></param>
        /// <param name="replaceExistingTranslations"></param>
        public void ImportFromCsv(string filePath, string lcid, bool replaceExistingTranslations, bool fallbackToDefaultLocale = false)
        {
            string csvData = File.ReadAllText(filePath);
            if (string.IsNullOrEmpty(csvData)) return;

            List<CsvRow> parsedRows = new List<CsvRow>();

            using (TextFieldParser parser = new TextFieldParser(new StringReader(csvData)))
            {
                parser.CommentTokens = new string[] { "#" };
                parser.SetDelimiters(new string[] { "," });
                parser.HasFieldsEnclosedInQuotes = true;

                /// Skip the header row.
                /// 
                parser.ReadFields();
                while (!parser.EndOfData)
                {
                    var textFields = parser.ReadFields();
                    if (textFields != null && textFields.Count() == 3)
                    {
                        parsedRows.Add(new CsvRow
                        {
                            Type = textFields[0],
                            Original = textFields[1],
                            Translation = (fallbackToDefaultLocale && string.IsNullOrEmpty(textFields[2]))? textFields[1] : textFields[2]
                        });
                    }
                }
            }

            ApplyTranslation(lcid, parsedRows, replaceExistingTranslations);
        }

        public void ImportFromResx(string filePath, string referencePath, string lcid, bool replaceExistingTranslations, bool fallbackToDefaultLocale)
        {
            List<CsvRow> parsedRows = new List<CsvRow>();

            using (ResXResourceReader defaultLocaleStrings = new ResXResourceReader(referencePath))
            using (ResXResourceSet translatedStrings = new ResXResourceSet(filePath))
            {
                foreach (DictionaryEntry kvp in defaultLocaleStrings)
                {
                    string key = kvp.Key.ToString();
                    string value = kvp.Value?.ToString();
                    string translation = translatedStrings.GetString(key);

                    if (fallbackToDefaultLocale && string.IsNullOrEmpty(translation))
                        translation = value;

                    if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(translation))
                    {
                        parsedRows.Add(new CsvRow
                        {
                            Type = string.Empty,
                            Original = value,
                            Translation = translation
                        });
                    }
                }
            }

            if (parsedRows.Count == 0)
                throw new NoResxMatchesException(filePath, referencePath);

            ApplyTranslation(lcid, parsedRows, replaceExistingTranslations);
        }

        /// <summary>
        /// Applies a list of translations to the ExpandoObject collections
        /// </summary>
        /// <param name="lcid"></param>
        /// <param name="translatedRows"></param>
        /// <param name="replaceExistingTranslations"></param>
        private void ApplyTranslation(string lcid, List<CsvRow> translatedRows, bool replaceExistingTranslations)
        {
            if (translatedRows == null || translatedRows.Count == 0)
                return;

            bool hasTypeInfo = !string.IsNullOrEmpty(translatedRows[0].Type);

            var allDataRows = GetAllDataRows();
            if(!MatchAllRows(allDataRows, lcid, translatedRows, replaceExistingTranslations))
            {
                /// Not all rows matched, so let's do this the slow way
                /// matching strings.
                /// 
                foreach(ExpandoObject row in allDataRows)
                {
                    var metaContainer = (MetadataObjectContainer)row.GetObject(ContainerColumnHeader);
                    var original = row.GetValue(DefaultCulture);
                    var csvRow = (hasTypeInfo)? translatedRows.Where(x => x.Type == metaContainer.TranslatedProperty.ToString() && x.Original.Equals(original)).FirstOrDefault() :
                        translatedRows.Where(x => x.Original.Equals(original)).FirstOrDefault();
                    if(csvRow != null)
                    {
                        row.SetValue(lcid, csvRow.Translation, replaceExistingTranslations);
                    }
                }
            }
        }

        /// <summary>
        /// Iterates over the dataRows and applies the translated strings with the assumption that
        /// translatedRows matches the dataRows in number and order.
        /// </summary>
        private bool MatchAllRows(List<ExpandoObject> dataRows, string lcid, List<CsvRow> translatedRows, bool replaceExistingTranslations)
        {
            if(dataRows == null || dataRows.Count != translatedRows?.Count)
                return false;

            for(int i = 0; i < translatedRows.Count; i++)
            {
                ExpandoObject row = dataRows[i];
                CsvRow csvRow = translatedRows[i];

                if (row.GetValue(DefaultCulture) != csvRow.Original)
                    return false;

                row.SetValue(lcid, csvRow.Translation, replaceExistingTranslations);
            }

            return true;
        }
    }
}
