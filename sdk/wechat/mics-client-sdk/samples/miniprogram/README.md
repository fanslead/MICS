# WeChat Mini Program sample

## 1) Prepare

在本仓库根目录启动 Gateway + HookMock（参考 `README.md`），确保可访问 WebSocket：

`ws://localhost:8080/ws?tenantId=t1&token=valid:u1&deviceId=dev1`

## 2) Install & build npm

在本目录执行：

```bash
npm install
```

然后用微信开发者工具打开本目录（项目根），执行「工具 → 构建 npm」。

## 3) Run

进入首页，点击 Connect，然后 Send。

