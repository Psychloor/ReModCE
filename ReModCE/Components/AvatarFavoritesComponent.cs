﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using Newtonsoft.Json;
using ReModCE.Core;
using ReModCE.Loader;
using ReModCE.Managers;
using ReModCE.UI;
using ReModCE.VRChat;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRC.SDKBase.Validation.Performance.Stats;
using AvatarList = Il2CppSystem.Collections.Generic.List<VRC.Core.ApiAvatar>;

namespace ReModCE.Components
{
    internal class AvatarFavoritesComponent : ModComponent, IAvatarListOwner
    {
        private ReAvatarList _avatarList;
        private ReUiButton _favoriteButton;

        private List<ReAvatar> _savedAvatars;
        private HttpClient _httpClient;
        private HttpClientHandler _httpClientHandler;

        private Button.ButtonClickedEvent _changeButtonEvent;

        private ConfigValue<bool> AvatarFavoritesEnabled;
        private ReQuickToggle _enabledToggle;
        private ConfigValue<int> MaxAvatarsPerPage;
        private ReQuickButton _maxAvatarsPerPageButton;

        private const string PinPath = "UserData/ReModCE/pin";
        private int _pinCode;
        private ReQuickButton _enterPinButton;

        private const string ApiUrl = "https://requi.dev/remod";

        public AvatarFavoritesComponent()
        {
            _httpClientHandler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            _httpClient = new HttpClient(_httpClientHandler);

            AvatarFavoritesEnabled = new ConfigValue<bool>(nameof(AvatarFavoritesEnabled), true);
            AvatarFavoritesEnabled.OnValueChanged += () =>
            {
                _enabledToggle.Toggle(AvatarFavoritesEnabled);
                _avatarList.GameObject.SetActive(AvatarFavoritesEnabled);
            };
            MaxAvatarsPerPage = new ConfigValue<int>(nameof(MaxAvatarsPerPage), 100);
            MaxAvatarsPerPage.OnValueChanged += () =>
            {
                _avatarList.SetMaxAvatarsPerPage(MaxAvatarsPerPage);
            };
            
            _savedAvatars = new List<ReAvatar>();

            if (File.Exists(PinPath))
            {
                if (!int.TryParse(File.ReadAllText(PinPath), out _pinCode))
                {
                    ReLogger.Warning($"Couldn't read pin file from \"{PinPath}\". File might be corrupted.");
                }
            }
        }

        public override void OnUiManagerInit(UiManager uiManager)
        {
            var menu = uiManager.MainMenu.GetSubMenu("Avatars");
            _enabledToggle = menu.AddToggle("Avatar Favorites", "Enable/Disable avatar favorites (requires VRC+)",
                AvatarFavoritesEnabled.SetValue, AvatarFavoritesEnabled);
            _maxAvatarsPerPageButton = menu.AddButton($"Max Avatars Per Page: {MaxAvatarsPerPage}",
                "Set the maximum amount of avatars shown per page",
                () =>
                {
                    VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowInputPopupWithCancel("Max Avatars Per Page",
                        MaxAvatarsPerPage.ToString(), InputField.InputType.Standard, true, "Submit",
                        new Action<string, Il2CppSystem.Collections.Generic.List<KeyCode>, Text>((s, k, t) =>
                        {
                            if (string.IsNullOrEmpty(s))
                                return;

                            if (!int.TryParse(s, out var maxAvatarsPerPage))
                                return;

                            MaxAvatarsPerPage.SetValue(maxAvatarsPerPage);
                            _maxAvatarsPerPageButton.Text = $"Max Avatars Per Page: {MaxAvatarsPerPage}";
                        }), null);
                });

            if (_pinCode == 0)
            {
                _enterPinButton = menu.AddButton("Set/Enter Pin", "Set or enter your pin for the ReMod CE API", () =>
                {
                    VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowInputPopupWithCancel("Enter pin",
                        "", InputField.InputType.Standard, true, "Submit",
                        new Action<string, Il2CppSystem.Collections.Generic.List<KeyCode>, Text>((s, k, t) =>
                        {
                            if (string.IsNullOrEmpty(s))
                                return;

                            if (!int.TryParse(s, out var pinCode))
                                return;

                            _pinCode = pinCode;
                            File.WriteAllText(PinPath, _pinCode.ToString());

                            _httpClientHandler = new HttpClientHandler
                            {
                                UseCookies = true,
                                CookieContainer = new CookieContainer()
                            };
                            _httpClient = new HttpClient(_httpClientHandler);

                            LoginToAPI(APIUser.CurrentUser);
                        }), null);
                });
            }
            
            _avatarList = new ReAvatarList("ReModCE Favorites", this);
            _avatarList.AvatarPedestal.field_Internal_Action_3_String_GameObject_AvatarPerformanceStats_0 = new Action<string, GameObject, AvatarPerformanceStats>(OnAvatarInstantiated);
            _avatarList.OnEnable += () =>
            {
                // make sure it stays off if it should be off.
                _avatarList.GameObject.SetActive(AvatarFavoritesEnabled);
            };

            _favoriteButton = new ReUiButton("Favorite", new Vector2(-600f, 375f), new Vector2(0.5f, 1f), () => FavoriteAvatar(_avatarList.AvatarPedestal.field_Internal_ApiAvatar_0),
                GameObject.Find("UserInterface/MenuContent/Screens/Avatar/Favorite Button").transform.parent);

            var changeButton = GameObject.Find("UserInterface/MenuContent/Screens/Avatar/Change Button");
            if (changeButton != null)
            {
                var button = changeButton.GetComponent<Button>();
                _changeButtonEvent = button.onClick;

                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(new Action(() =>
                {
                    var currentAvatar = _avatarList.AvatarPedestal.field_Internal_ApiAvatar_0;
                    if (!HasAvatarFavorited(currentAvatar.id)) // this isn't in our list. we don't care about it
                    {
                        _changeButtonEvent.Invoke();
                        return;
                    }
                    
                    new ApiAvatar { id = currentAvatar.id }.Fetch(new Action<ApiContainer>(ac =>
                    {
                        var updatedAvatar = ac.Model.Cast<ApiAvatar>();
                        switch (updatedAvatar.releaseStatus)
                        {
                            case "private" when updatedAvatar.authorId != APIUser.CurrentUser.id:
                                VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowAlert("ReMod CE", "This avatar is private and you don't own it. You can't switch into it.");
                                break;
                            case "unavailable":
                                VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowAlert("ReMod CE", "This avatar has been deleted. You can't switch into it.");
                                break;
                            default:
                                _changeButtonEvent.Invoke();
                                break;
                        }
                    }), new Action<ApiContainer>(ac =>
                    {
                        VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowAlert("ReMod CE", "This avatar has been deleted. You can't switch into it.");
                    }));
                }));
            }

            if (uiManager.IsRemodLoaded || uiManager.IsRubyLoaded)
            {
                _favoriteButton.Position += new Vector3(UiManager.ButtonSize, 0f);
            }
            
            MelonCoroutines.Start(LoginToAPICoroutine());
        }

        private IEnumerator LoginToAPICoroutine()
        {
            while (APIUser.CurrentUser == null) yield return new WaitForEndOfFrame();

            var user = APIUser.CurrentUser;
            LoginToAPI(user);
        }

        private async void LoginToAPI(APIUser user)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/login.php")
            {
                Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                {
                    new("user_id", user.id),
                    new("pin", _pinCode.ToString())
                })
            };

            var loginResponse = await _httpClient.SendAsync(request);
            if (loginResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                var errorData = await loginResponse.Content.ReadAsStringAsync();
                var errorMessage = JsonConvert.DeserializeObject<ApiError>(errorData).Error;

                ReLogger.Error($"Could not login to ReMod CE API: \"{errorMessage}\"");
                MelonCoroutines.Start(ShowAlertDelayed($"Could not login to ReMod CE API\nReason: \"{errorMessage}\""));
                File.Delete(PinPath);
                return;
            }

            if (_pinCode != 0 && _enterPinButton != null)
            {
                _enterPinButton.Interactable = false;
            }

            FetchAvatars();
        }

        private async void FetchAvatars()
        {
            var avatarResponse = SendAvatarRequest(HttpMethod.Get).Result;
            if (!avatarResponse.IsSuccessStatusCode)
            {
                var errorData = await avatarResponse.Content.ReadAsStringAsync();
                var errorMessage = JsonConvert.DeserializeObject<ApiError>(errorData).Error;

                ReLogger.Error($"Could not fetch avatars: \"{errorMessage}\"");
                return;
            }

            var avatars = await avatarResponse.Content.ReadAsStringAsync();
            _savedAvatars = JsonConvert.DeserializeObject<List<ReAvatar>>(avatars);
        }

        private static IEnumerator ShowAlertDelayed(string message, float seconds = 0.5f)
        {
            if (VRCUiPopupManager.prop_VRCUiPopupManager_0 == null) yield break;

            yield return new WaitForSeconds(seconds);

            VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowAlert("ReMod CE", message);
        }

        private void OnAvatarInstantiated(string url, GameObject avatar, AvatarPerformanceStats avatarPerformanceStats)
        {
            _favoriteButton.Text = HasAvatarFavorited(_avatarList.AvatarPedestal.field_Internal_ApiAvatar_0.id) ? "Unfavorite" : "Favorite";
        }

        private void FavoriteAvatar(ApiAvatar apiAvatar)
        {
            var isSupporter = APIUser.CurrentUser.isSupporter;
            if (!isSupporter)
            {
                VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowAlert("ReMod CE", "You need VRC+ to use this feature.\nWe're not trying to destroy VRChat's monetization.");
                return;
            }

            var hasFavorited = HasAvatarFavorited(apiAvatar.id);

            var favResponse =
                SendAvatarRequest(hasFavorited ? HttpMethod.Delete : HttpMethod.Put, new ReAvatar(apiAvatar)).Result;

            if (!favResponse.IsSuccessStatusCode)
            {
                favResponse.Content.ReadAsStringAsync().ContinueWith(errorData =>
                {
                    var errorMessage = JsonConvert.DeserializeObject<ApiError>(errorData.Result).Error;

                    ReLogger.Error($"Could not (un)favorite avatar: \"{errorMessage}\"");
                    if (favResponse.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        MelonCoroutines.Start(ShowAlertDelayed($"Could not (un)favorite avatar\nReason: \"{errorMessage}\""));
                    }
                });
            }
            else
            {
                if (!hasFavorited)
                {
                    _savedAvatars.Insert(0, new ReAvatar(apiAvatar));
                    _favoriteButton.Text = "Unfavorite";
                }
                else
                {
                    _savedAvatars.RemoveAll(a => a.Id == apiAvatar.id);
                    _favoriteButton.Text = "Favorite";
                }
            }

            _avatarList.Refresh(GetAvatars());
        }

        private async Task<HttpResponseMessage> SendAvatarRequest(HttpMethod method, ReAvatar avater = null)
        {
            var request = new HttpRequestMessage(method, $"{ApiUrl}/avatar.php");
            if (avater != null)
            {
                request.Content = new StringContent(avater.ToJson(), Encoding.UTF8, "application/json");
            }

            return await _httpClient.SendAsync(request);
        }

        private bool HasAvatarFavorited(string id)
        {
            return _savedAvatars.FirstOrDefault(a => a.Id == id) != null;
        }

        public AvatarList GetAvatars()
        {
            var list = new AvatarList();
            foreach (var avi in _savedAvatars.Distinct().Select(x => x.AsApiAvatar()).ToList())
            {
                list.Add(avi);
            }
            return list;
        }
    }
}
