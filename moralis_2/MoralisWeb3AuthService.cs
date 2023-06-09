using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using PlayFab;
using PlayFab.CloudScriptModels;
using WalletConnectSharp.Unity;
using Assets.Scripts.Moralis;

public class MoralisWeb3AuthService : MonoBehaviour
{
    //  Events ----------------------------------------

    /// <summary>
    /// Invoked when authentication was 
    /// 
    [Header("Events")] public UnityEvent OnSuccess = new UnityEvent();

    /// <summary>
    /// Invoked when State==AuthenticationKitState.Disconnected
    /// 
    public UnityEvent OnFailed = new UnityEvent();

    private AuthenticationKit authenticationKit = null;

    public void Awake()
    {
        authenticationKit = FindObjectOfType<AuthenticationKit>(true);
    }

    public void StateObservable_OnValueChanged(AuthenticationKitState authenticationKitState)
    {
        switch (authenticationKitState)
        {
            case AuthenticationKitState.WalletConnected:

#if !UNITY_WEBGL
                // Get the address and chain ID with WalletConnect 
                string address = WalletConnect.ActiveSession.Accounts[0];
                int chainid = WalletConnect.ActiveSession.ChainId;
#else
                // Get the address and chain ID with Web3 
                string address = Web3GL.Account().ToLower();
                int chainid = Web3GL.ChainId();
#endif
                // Create sign message 
                CreateMessage(address, chainid);
                break;
        }
    }

    private void CreateMessage(string address, int chainid)
    {
        // Get message from Moralis with PlayFab Azure Functions 
        PlayFabCloudScriptAPI.ExecuteFunction(new ExecuteFunctionRequest()
        {
            Entity = new PlayFab.CloudScriptModels.EntityKey()
            {
                Id = PlayFabSettings.staticPlayer.EntityId, //Get this from when you logged in,
                Type = PlayFabSettings.staticPlayer.EntityType, //Get this from when you logged in
            },
            FunctionName = "ChallengeRequest", //This should be the name of your Azure Function that you created.
            FunctionParameter =
                new Dictionary<string, object>() //This is the data that you would want to pass into your function.
                {
                    { "address", address },
                    { "chainid", chainid }
                },
            GeneratePlayStreamEvent = true //Set this to true if you would like this call to show up in PlayStream
        }, async (ExecuteFunctionResult result) =>
        {
            if (result.FunctionResultTooLarge ?? false)
            {
                Debug.Log(
                    "This can happen if you exceed the limit that can be returned from an Azure Function; see PlayFab Limits Page for details.");
                // If there is an error, fire the OnFailed event
                OnFailed.Invoke();
                return;
            }

            // Check if we got a message
            string message = result.FunctionResult.ToString();
            if (!String.IsNullOrEmpty(message))
            {
                authenticationKit.State = AuthenticationKitState.WalletSigning;
                
#if !UNITY_WEBGL
                // Sign the message with WalletConnect
                string signature = await WalletConnect.ActiveSession.EthPersonalSign(address, message);
#else
                // Sign the message with Web3
                string signature = await Web3GL.Sign(message);
#endif
                if (!String.IsNullOrEmpty(signature))
                {
                    // Send the message and signature to the Authenticate Azure function for validation
                    Authenticate(message, signature);
                }
                else
                {
                    // If there is no signature, fire the OnFailed event
                    OnFailed.Invoke();
                }
            }
            else
            {
                // If there is no message, fire the OnFailed event
                OnFailed.Invoke();
            }
        }, (PlayFabError error) =>
        {
            Debug.Log($"Oops Something went wrong: {error.GenerateErrorReport()}");
            // If there is an error, fire the OnFailed event
            OnFailed.Invoke();
        });
    }

    private void Authenticate(string message, string signature)
    {
        // Send the message and signature to the Authenticate Azure function for validation
        PlayFabCloudScriptAPI.ExecuteFunction(new ExecuteFunctionRequest()
        {
            Entity = new PlayFab.CloudScriptModels.EntityKey()
            {
                Id = PlayFabSettings.staticPlayer.EntityId, //Get this from when you logged in,
                Type = PlayFabSettings.staticPlayer.EntityType, //Get this from when you logged in
            },
            FunctionName = "ChallengeVerify", //This should be the name of your Azure Function that you created.
            FunctionParameter =
                new Dictionary<string, object>() //This is the data that you would want to pass into your function.
                {
                    { "message", message },
                    { "signature", signature }
                },
            GeneratePlayStreamEvent = true //Set this to true if you would like this call to show up in PlayStream
        }, (ExecuteFunctionResult result) =>
        {
            if (result.FunctionResultTooLarge ?? false)
            {
                Debug.Log(
                    "This can happen if you exceed the limit that can be returned from an Azure Function; see PlayFab Limits Page for details.");
                // If there is an error, fire the OnFailed event
                OnFailed.Invoke();
                return;
            }

            // If the authentication succeeded, the user profile is updated and we get the UpdateUserDataAsync return values as response
            // If it fails it returns empty
            if (!String.IsNullOrEmpty(result.FunctionResult.ToString()))
            {
                authenticationKit.State = AuthenticationKitState.WalletSigned;
                
                // On success, fire the OnSuccess event
                OnSuccess.Invoke();
            }
            else
            {
                // If the response is empty, fire the OnFailed event
                OnFailed.Invoke();
            }
        }, (PlayFabError error) =>
        {
            Debug.Log($"Oops Something went wrong: {error.GenerateErrorReport()}");
            // If the is a error fire the OnFailed event
            OnFailed.Invoke();
        });
    }
}