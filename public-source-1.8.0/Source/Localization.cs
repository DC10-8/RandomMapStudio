using System;
using System.IO;
using System.Globalization;

namespace RandomMapStudio
{
    public static class L
    {
        static volatile bool japanese=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ja";
        static bool loaded;
        static string PreferencePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RandomMapStudio.language.txt"); } }
        public static bool Japanese
        {
            get
            {
                if(!loaded)
                {
                    loaded=true;
                    try { if(File.Exists(PreferencePath))japanese=File.ReadAllText(PreferencePath).Trim()=="ja"; } catch(IOException){} catch(UnauthorizedAccessException){}
                }
                return japanese;
            }
        }
        public static string T(string ja,string en) { return Japanese?ja:en; }
        public static void Select(int index,bool persist=true)
        {
            if(index!=0 && index!=1)throw new ArgumentOutOfRangeException("index");
            japanese=index==0;loaded=true;
            if(!persist)return;
            try { File.WriteAllText(PreferencePath,japanese?"ja":"en"); } catch(IOException){} catch(UnauthorizedAccessException){}
        }
    }
}
