---
sidebar_position: 2
---

# Deploy to Lambda

Strands Agents .NET agents can be deployed to AWS Lambda as NativeAOT binaries on the `provided.al2023` runtime.

## Quick reference

```bash
# 1. Add AOT settings to your .csproj
# <PublishAot>true</PublishAot>
# <InvariantGlobalization>true</InvariantGlobalization>

# 2. Build on Linux (required for AOT)
dotnet publish -c Release -r linux-x64 --output ./publish

# 3. Package
cp ./publish/YourApp ./bootstrap
zip -j function.zip bootstrap

# 4. Deploy
aws lambda create-function \
  --function-name my-agent \
  --runtime provided.al2023 \
  --handler bootstrap \
  --role arn:aws:iam::ACCOUNT:role/ROLE \
  --zip-file fileb://function.zip \
  --memory-size 512 \
  --timeout 30
```

For a complete walkthrough, see the **[Deploy to Lambda with AOT tutorial](../tutorials/aot-lambda)**.

For a multi-step durable pipeline, see the **[DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow)**.
