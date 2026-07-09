#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using VRC.SDK3.Video.Components.AVPro;

namespace PasocomMate.AunCast.Internal
{
    public partial class AunCastSettingsInspector
    {
        private void OnDisable()
        {
            StopVpmVersionCheck();
        }


        private void EnsureVpmVersionCheckStarted()
        {
            LoadVpmCheckResultFromSessionCache();
            if (_vpmVersionCheckRequested || _vpmVersionCheckInProgress) return;

            string packageName = GetCurrentPackageName();
            string currentVersion = GetCurrentPackageVersion();
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(currentVersion) || currentVersion == "unknown")
                return;

            string listingUrl = GetVpmListingUrl();
            if (string.IsNullOrEmpty(listingUrl))
                return;

            _vpmVersionCheckRequested = true;
            _vpmVersionCheckInProgress = true;
            _vpmVersionRequestStartTime = EditorApplication.timeSinceStartup;
            _vpmVersionRequest = UnityWebRequest.Get(listingUrl);
            _vpmVersionRequest.SendWebRequest();
            Repaint();
        }

        private void PollVpmVersionCheck()
        {
            if (!_vpmVersionCheckInProgress || _vpmVersionRequest == null) return;

            if (!HasVpmRequestTimedOut() && !_vpmVersionRequest.isDone)
            {
                Repaint();
                return;
            }

            string currentVersion = GetCurrentPackageVersion();
            string packageName = GetCurrentPackageName();

            if (HasVpmRequestTimedOut())
            {
                MarkVpmCheckCompletedForSession();
                StopVpmVersionCheck();
                return;
            }

#if UNITY_2020_2_OR_NEWER
            bool success = _vpmVersionRequest.result == UnityWebRequest.Result.Success;
#else
            bool success = !_vpmVersionRequest.isNetworkError && !_vpmVersionRequest.isHttpError;
#endif
            if (!success)
            {
                MarkVpmCheckCompletedForSession();
                StopVpmVersionCheck();
                return;
            }

            string json = _vpmVersionRequest.downloadHandler != null
                ? _vpmVersionRequest.downloadHandler.text
                : string.Empty;
            if (TryExtractLatestVersionFromVpmListing(json, packageName, out var latestVersion))
            {
                _latestVersion = latestVersion;
                _hasVersionUpdate = IsNewerVersion(latestVersion, currentVersion);
            }

            MarkVpmCheckCompletedForSession();
            StopVpmVersionCheck();
        }

        private bool HasVpmRequestTimedOut()
        {
            if (!_vpmVersionCheckInProgress) return false;
            return EditorApplication.timeSinceStartup - _vpmVersionRequestStartTime > VPM_VERSION_REQUEST_TIMEOUT_SEC;
        }

        private void StopVpmVersionCheck()
        {
            _vpmVersionCheckInProgress = false;
            if (_vpmVersionRequest != null)
            {
                if (!_vpmVersionRequest.isDone)
                    _vpmVersionRequest.Abort();
                _vpmVersionRequest.Dispose();
                _vpmVersionRequest = null;
            }
        }

        private void LoadVpmCheckResultFromSessionCache()
        {
            if (_vpmSessionCacheLoaded) return;
            _vpmSessionCacheLoaded = true;

            if (!SessionState.GetBool(SESSION_KEY_VPM_CHECK_DONE, false))
                return;

            _vpmVersionCheckRequested = true;
            _hasVersionUpdate = SessionState.GetBool(SESSION_KEY_VPM_HAS_UPDATE, false);
            _latestVersion = SessionState.GetString(SESSION_KEY_VPM_LATEST_VERSION, string.Empty);
        }

        private void MarkVpmCheckCompletedForSession()
        {
            _vpmVersionCheckRequested = true;
            SessionState.SetBool(SESSION_KEY_VPM_CHECK_DONE, true);
            SessionState.SetBool(SESSION_KEY_VPM_HAS_UPDATE, _hasVersionUpdate);
            SessionState.SetString(SESSION_KEY_VPM_LATEST_VERSION, _latestVersion ?? string.Empty);
        }

        private string GetVpmListingUrl()
        {
            return DEFAULT_VPM_LISTING_URL;
        }

        private static bool TryExtractLatestVersionFromVpmListing(
            string json,
            string packageName,
            out string latestVersion)
        {
            latestVersion = string.Empty;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(packageName)) return false;

            int packagesObjectStart = FindObjectStartByKey(json, "packages", 0);
            if (packagesObjectStart < 0) return false;
            if (!TryFindMatchingBrace(json, packagesObjectStart, out int packagesObjectEnd)) return false;

            int packageKeyIndex = json.IndexOf($"\"{packageName}\"", packagesObjectStart, StringComparison.Ordinal);
            if (packageKeyIndex < 0 || packageKeyIndex > packagesObjectEnd) return false;

            int packageObjectStart = FindObjectStartByKey(json, packageName, packageKeyIndex);
            if (packageObjectStart < 0 || packageObjectStart > packagesObjectEnd) return false;
            if (!TryFindMatchingBrace(json, packageObjectStart, out int packageObjectEnd)) return false;

            int versionsObjectStart = FindObjectStartByKey(json, "versions", packageObjectStart);
            if (versionsObjectStart < 0 || versionsObjectStart > packageObjectEnd) return false;
            if (!TryFindMatchingBrace(json, versionsObjectStart, out int versionsObjectEnd)) return false;

            return TryGetHighestSemverKey(json, versionsObjectStart, versionsObjectEnd, out latestVersion);
        }

        private static int FindObjectStartByKey(string json, string key, int searchStartIndex)
        {
            int keyIndex = json.IndexOf($"\"{key}\"", searchStartIndex, StringComparison.Ordinal);
            if (keyIndex < 0) return -1;

            int colonIndex = json.IndexOf(':', keyIndex);
            if (colonIndex < 0) return -1;

            for (int i = colonIndex + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (char.IsWhiteSpace(c)) continue;
                return c == '{' ? i : -1;
            }

            return -1;
        }

        private static bool TryFindMatchingBrace(string text, int objectStartIndex, out int objectEndIndex)
        {
            objectEndIndex = -1;
            if (objectStartIndex < 0 || objectStartIndex >= text.Length || text[objectStartIndex] != '{')
                return false;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = objectStartIndex; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    continue;
                }

                if (c != '}') continue;
                depth--;
                if (depth != 0) continue;

                objectEndIndex = i;
                return true;
            }

            return false;
        }

        private static bool TryGetHighestSemverKey(
            string json,
            int objectStartIndex,
            int objectEndIndex,
            out string highestVersion)
        {
            highestVersion = string.Empty;
            if (objectStartIndex < 0 || objectEndIndex <= objectStartIndex) return false;

            int i = objectStartIndex + 1;
            while (i < objectEndIndex)
            {
                if (json[i] != '"')
                {
                    i++;
                    continue;
                }

                int keyStart = i + 1;
                int keyEnd = keyStart;
                bool escaped = false;
                for (; keyEnd < objectEndIndex; keyEnd++)
                {
                    char c = json[keyEnd];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"') break;
                }

                if (keyEnd >= objectEndIndex) break;

                string key = json.Substring(keyStart, keyEnd - keyStart);
                i = keyEnd + 1;
                while (i < objectEndIndex && char.IsWhiteSpace(json[i])) i++;
                if (i >= objectEndIndex || json[i] != ':') continue;
                i++;

                if (!TryParseVersion(key, out _)) continue;
                if (string.IsNullOrEmpty(highestVersion) || IsNewerVersion(key, highestVersion))
                    highestVersion = key;
            }

            return !string.IsNullOrEmpty(highestVersion);
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrEmpty(latest) || string.IsNullOrEmpty(current)) return false;
            if (string.Equals(latest, current, StringComparison.Ordinal)) return false;

            if (TryParseVersion(latest, out var latestVersionObj) &&
                TryParseVersion(current, out var currentVersionObj))
            {
                return latestVersionObj > currentVersionObj;
            }

            return string.Compare(latest, current, StringComparison.Ordinal) > 0;
        }

        private static bool TryParseVersion(string raw, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(raw)) return false;

            string normalized = raw;
            int plusIndex = normalized.IndexOf('+');
            if (plusIndex >= 0)
                normalized = normalized.Substring(0, plusIndex);

            int dashIndex = normalized.IndexOf('-');
            if (dashIndex >= 0)
                normalized = normalized.Substring(0, dashIndex);

            return Version.TryParse(normalized, out version);
        }

        private string GetCurrentPackageName()
        {
            return AunCastInspectorBanner.GetCurrentPackageName(this);
        }

        private string GetCurrentPackageVersion()
        {
            return AunCastInspectorBanner.GetCurrentPackageVersion(this);
        }

    }
}
#endif
