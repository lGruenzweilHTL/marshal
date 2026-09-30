# Sandbox image the executor host runs per session (executorHost.image, default marshal-claude-sandbox).
# docker build -t marshal-claude-sandbox src/Marshal.ExecutorHost/sandbox
FROM node:22-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && npm install -g @anthropic-ai/claude-code \
    && git config --system safe.directory "*"
# The worktree is mounted at its host path and used as the working directory; the model API key comes from the
# environment file the executor host passes (never baked into the image).
USER node
