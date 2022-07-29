﻿using ABI.CCK.Scripts;
using ABI_RC.Core;
using ABI_RC.Core.Player;
using ABI_RC.Core.Savior;
using Bespoke.Osc;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Net;

namespace CVROSC
{
    public sealed class CVROSCMod : MelonMod
    {
        static VRLayer OSCServer = null;

        const string Base = "/avatar/parameters";
        static float TAL = -1.0f;
        static string AvatarGUID = "0";
        static CVRAnimatorManager AnimatorManager = null;
        static List<CVRAdvancedSettingsFileProfileValue> Parameters = null;

        public override void OnUpdate()
        {
            if (OSCServer == null)
            {
                OSCServer = new VRLayer();
                OSCServer.Init(new EventHandler<OscMessageReceivedEventArgs>(MessageF), new EventHandler<OscBundleReceivedEventArgs>(BundleF));

                MelonLogger.Msg("Started Open Sound Control server. Receiving on {0} and sending on {1}.", OSCServer.VRServer.Port, OSCServer.VRClient.Port);
            }

            try
            {
                if (TAL != PlayerSetup.Instance.timeAvatarLoaded || AnimatorManager == null)
                {
                    if (TAL != PlayerSetup.Instance.timeAvatarLoaded)
                        MelonLogger.Msg(String.Format("Avatar change detected, loading animation manager... (OT{0}, NT{1})", TAL, PlayerSetup.Instance.timeAvatarLoaded));

                    if (AnimatorManager == null)
                        AnimatorManager = PlayerSetup.Instance.animatorManager;

                    Parameters = AnimatorManager.GetAdditionalSettingsCurrent();
                    TAL = PlayerSetup.Instance.timeAvatarLoaded;
                    AvatarGUID = MetaPort.Instance.currentAvatarGuid;

                    MelonLogger.Msg(String.Format("Animation manager found and cached, {0} parameters found!", Parameters.Count));

                    if (Parameters.Count > 0)
                    {
                        MelonLogger.Msg(String.Format("Scanning parameters for {0}...", AvatarGUID));

                        foreach (CVRAdvancedSettingsFileProfileValue Parameter in Parameters)
                            MelonLogger.Msg(String.Format("Parameter {0}: {1}", Parameter.name, Parameter.value));

                        MelonLogger.Msg("The new animation manager is now ready to be controlled through Open Sound Control.");
                    }

                    OSCServer.SendMsg("/avatar/change", OSCServer.VRClient, AvatarGUID, true);
                }

                if (AnimatorManager != null && Parameters != null)
                {
                    foreach (CVRAdvancedSettingsFileProfileValue Parameter in Parameters)
                    {
                        float? f = AnimatorManager.GetAnimatorParameterFloat(Parameter.name);
                        if (f != null)
                        {
                            if (f != Parameter.value)
                            {
                                Parameter.value = (float)f;
                                OSCServer.SendMsg(String.Format("{0}/{1}", Base, Parameter.name), OSCServer.VRClient, Parameter.value, true);
                                continue;
                            }
                        }

                        int? i = AnimatorManager.GetAnimatorParameterInt(Parameter.name);
                        if (i != null)
                        {
                            if (i != (int)Parameter.value)
                            {                              
                                Parameter.value = (float)i;
                                OSCServer.SendMsg(String.Format("{0}/{1}", Base, Parameter.name), OSCServer.VRClient, Parameter.value, true);
                                continue;
                            }
                        }

                        bool? b = AnimatorManager.GetAnimatorParameterBool(Parameter.name);
                        if (b != null)
                        {
                            if (b != (Parameter.value == 1f))
                            {
                                Parameter.value = Convert.ToSingle(b);
                                OSCServer.SendMsg(String.Format("{0}/{1}", Base, Parameter.name), OSCServer.VRClient, Parameter.value, true);
                                continue;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore until we find a valid avatar
                AnimatorManager = null;
                Parameters = null;
            }
        }

        static void SetParameter(string Address, object Data)
        {
            switch (Data)
            {
                case bool b:
                case int i:
                case float f:
                    if (Address.StartsWith("/avatar/parameters"))
                    {
                        string Variable = Address.Substring(Address.LastIndexOf("/") + 1);
                        // MelonLogger.Msg("Received message {0} with data {1}! ({2})", Variable, Data[0], Address);

                        foreach (CVRAdvancedSettingsFileProfileValue Parameter in Parameters)
                        {
                            if (Parameter.name.Equals(Variable))
                            {
                                Parameter.value = (float)Data;
                                AnimatorManager.SetAnimatorParameter(Parameter.name, Parameter.value);
                            }
                        }              
                    }
                    break;

                default:
                    MelonLogger.Error("Received unsupported message at address {0} of type {1}, with value {2}.",
                        Address, Data.GetType().Name, Data);
                    return;
            }
        }

        static void AnalyzeData(object sender, IPEndPoint Source, string Address, object Data)
        {
            if (sender == null) return;

            switch (Data)
            {
                case OscBundle OSCB:
                    foreach (OscMessage bOSCM in OSCB.Messages)
                        SetParameter(Address, bOSCM.Data[0]);

                    foreach (OscBundle bOSCB in OSCB.Bundles)
                        AnalyzeData(sender, Source, Address, bOSCB);

                    break;

                case OscMessage OSCM:
                    SetParameter(Address, OSCM.Data[0]);
                    break;

                default:
                    MelonLogger.Error("Received unsupported packet at address {0}!", Address);
                    return;
            }       
        }

        static void BundleF(object sender, OscBundleReceivedEventArgs Var)
        {
            AnalyzeData(sender, Var.Bundle.SourceEndPoint, Var.Bundle.Address, Var.Bundle);
        }

        static void MessageF(object sender, OscMessageReceivedEventArgs Var)
        {
            AnalyzeData(sender, Var.Message.SourceEndPoint, Var.Message.Address, Var.Message);
        }
    }
}