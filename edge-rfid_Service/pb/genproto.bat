rem protoc --csharp_out=.. --proto_path=. EdgeMsg.proto
for %%i in (*.proto) do protoc --csharp_out=.. --proto_path=. %%i