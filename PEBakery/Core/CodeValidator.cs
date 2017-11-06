﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PEBakery.Core
{
    public class CodeValidator
    {
        #region Field and Property
        private Plugin p;
        private List<PluginSection> visitedSections = new List<PluginSection>();

        public int CodeSectionCount => p.Sections.Where(x => x.Value.Type == SectionType.Code).Count();
        public int VisitedSectionCount => visitedSections.Count;
        public double Coverage
        {
            get
            {
                if (CodeSectionCount == 0)
                    return 0;
                else
                    return (double) VisitedSectionCount / CodeSectionCount;
            }
        }

        private List<LogInfo> logInfos = new List<LogInfo>();
        public LogInfo[] LogInfos
        {
            get
            { // Call .ToArray to get logInfo's copy 
                LogInfo[] list = logInfos.ToArray();
                logInfos.Clear();
                return list;
            }
        }
        #endregion

        #region Constructor
        public CodeValidator(Plugin p)
        {
            this.p = p ?? throw new ArgumentNullException("p");
        }
        #endregion

        #region 
        public void Validate()
        {
            // Codes
            if (p.Sections.ContainsKey("Process"))
                logInfos.AddRange(ValidateCodeSection(p.Sections["Process"]));

            // UICodes
            if (p.Sections.ContainsKey("Interface"))
                logInfos.AddRange(ValidateUISection(p.Sections["Interface"]));

            if (p.MainInfo.ContainsKey("Interface"))
            {
                string ifaceSection = p.MainInfo["Interface"];
                if (p.Sections.ContainsKey(ifaceSection))
                    logInfos.AddRange(ValidateUISection(p.Sections[ifaceSection]));
            }
        }

        #region ValidateCodeSection
        private List<LogInfo> ValidateCodeSection(PluginSection section)
        {
            // Already processed, so skip
            if (visitedSections.Contains(section))
                return new List<LogInfo>();

            // Force parsing of code, bypassing caching by section.GetCodes()
            List<string> lines = section.GetLines();
            SectionAddress addr = new SectionAddress(p, section);
            List<CodeCommand> codes = CodeParser.ParseRawLines(lines, addr, out List<LogInfo> logs);

            visitedSections.Add(section);
            InternalValidateCodes(codes, logs);

            return logs;
        }

        private void InternalValidateCodes(List<CodeCommand> codes, List<LogInfo> logs)
        {
            foreach (CodeCommand cmd in codes)
            {
                switch (cmd.Type)
                {
                    case CodeType.If:
                        {
                            Debug.Assert(cmd.Info.GetType() == typeof(CodeInfo_If));
                            CodeInfo_If info = cmd.Info as CodeInfo_If;

                            InternalValidateCodes(info.Link, logs);
                        }
                        break;
                    case CodeType.Else:
                        {
                            Debug.Assert(cmd.Info.GetType() == typeof(CodeInfo_Else));
                            CodeInfo_Else info = cmd.Info as CodeInfo_Else;

                            InternalValidateCodes(info.Link, logs);
                        }
                        break;
                    case CodeType.Run:
                    case CodeType.Exec:
                        {
                            Debug.Assert(cmd.Info.GetType() == typeof(CodeInfo_RunExec));
                            CodeInfo_RunExec info = cmd.Info as CodeInfo_RunExec;

                            // CodeValidator does not have Variable information, so just check with predefined literal
                            if (info.PluginFile.Equals("%ScriptFile%", StringComparison.OrdinalIgnoreCase) ||
                                info.PluginFile.Equals("%PluginFile%", StringComparison.OrdinalIgnoreCase))
                            {
                                if (p.Sections.ContainsKey(info.SectionName))
                                {
                                    logs.AddRange(ValidateCodeSection(p.Sections[info.SectionName]));
                                }
                                else
                                {
                                    MatchCollection matches = Regex.Matches(info.SectionName, @"%([^ %]+)%", RegexOptions.Compiled);
                                    if (matches.Count == 0)
                                        logs.Add(new LogInfo(LogState.Error, $"Section [{info.SectionName}] does not exist", cmd));
                                }
                            }
                        }
                        break;
                    case CodeType.Loop:
                        {
                            Debug.Assert(cmd.Info.GetType() == typeof(CodeInfo_Loop));
                            CodeInfo_Loop info = cmd.Info as CodeInfo_Loop;

                            if (info.Break)
                                continue;

                            // CodeValidator does not have Variable information, so just check with predefined literal
                            if (info.PluginFile.Equals("%ScriptFile%", StringComparison.OrdinalIgnoreCase) ||
                                info.PluginFile.Equals("%PluginFile%", StringComparison.OrdinalIgnoreCase))
                            {
                                if (p.Sections.ContainsKey(info.SectionName))
                                {
                                    logs.AddRange(ValidateCodeSection(p.Sections[info.SectionName]));
                                }
                                else
                                {
                                    MatchCollection matches = Regex.Matches(info.SectionName, @"%([^ %]+)%", RegexOptions.Compiled);
                                    if (matches.Count == 0)
                                        logs.Add(new LogInfo(LogState.Error, $"Section [{info.SectionName}] does not exist", cmd));
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region ValidateUISection
        private List<LogInfo> ValidateUISection(PluginSection section)
        {
            // Force parsing of code, bypassing caching by section.GetUICodes()
            List<string> lines = section.GetLines();
            SectionAddress addr = new SectionAddress(p, section);
            List<UICommand> uiCodes = UIParser.ParseRawLines(lines, addr, out List<LogInfo> logs);

            foreach (UICommand uiCmd in uiCodes)
            {
                switch (uiCmd.Type)
                {
                    case UIType.CheckBox:
                        {
                            Debug.Assert(uiCmd.Info.GetType() == typeof(UIInfo_CheckBox));
                            UIInfo_CheckBox info = uiCmd.Info as UIInfo_CheckBox;

                            if (info.SectionName != null)
                            {
                                if (p.Sections.ContainsKey(info.SectionName)) // Only if section exists
                                    logs.AddRange(ValidateCodeSection(p.Sections[info.SectionName]));
                            }
                        }
                        break;
                    case UIType.Button:
                        {
                            Debug.Assert(uiCmd.Info.GetType() == typeof(UIInfo_Button));
                            UIInfo_Button info = uiCmd.Info as UIInfo_Button;

                            if (info.SectionName != null)
                            {
                                if (p.Sections.ContainsKey(info.SectionName)) // Only if section exists
                                    logs.AddRange(ValidateCodeSection(p.Sections[info.SectionName]));
                            }
                        }
                        break;
                    case UIType.RadioButton:
                        {
                            Debug.Assert(uiCmd.Info.GetType() == typeof(UIInfo_RadioButton));
                            UIInfo_RadioButton info = uiCmd.Info as UIInfo_RadioButton;

                            if (info.SectionName != null)
                            {
                                if (p.Sections.ContainsKey(info.SectionName)) // Only if section exists
                                    logs.AddRange(ValidateCodeSection(p.Sections[info.SectionName]));
                            }
                        }
                        break;
                }
            }

            return logs;
        }
        #endregion

        #endregion
    }
}
