package com.mics.hooksdk.http;

import com.mics.contracts.hook.v1.AuthRequest;
import com.mics.contracts.hook.v1.AuthResponse;
import com.mics.contracts.hook.v1.CheckMessageRequest;
import com.mics.contracts.hook.v1.CheckMessageResponse;
import com.mics.contracts.hook.v1.GetGroupMembersRequest;
import com.mics.contracts.hook.v1.GetGroupMembersResponse;
import com.mics.contracts.hook.v1.GetOfflineMessagesRequest;
import com.mics.contracts.hook.v1.GetOfflineMessagesResponse;

public interface MicsHookHandler {
    AuthResponse onAuth(AuthRequest request) throws Exception;

    CheckMessageResponse onCheckMessage(CheckMessageRequest request) throws Exception;

    GetGroupMembersResponse onGetGroupMembers(GetGroupMembersRequest request) throws Exception;

    default GetOfflineMessagesResponse onGetOfflineMessages(GetOfflineMessagesRequest request) throws Exception {
        return GetOfflineMessagesResponse.newBuilder()
                .setOk(true)
                .build();
    }
}
