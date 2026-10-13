# Spec Delta

## ADDED Requirements

### Requirement: Reasoning effort
Each configured model SHALL have a `reasoning` setting of `off`, `low`, `medium` or `high`, defaulting to `off`. A model call SHALL request reasoning at the configured effort for text turns and for background task runs. A model call for a voice turn SHALL request no reasoning, whatever the setting. A model whose provider cannot return reasoning SHALL be called as if the setting were `off`, and startup SHALL log a warning naming the model.

#### Scenario: Text turn with reasoning
- **WHEN** a text turn routes to a model configured with `reasoning: medium`
- **THEN** the model call requests medium reasoning effort

#### Scenario: Voice turn
- **WHEN** a voice turn routes to the same model
- **THEN** the model call requests no reasoning

#### Scenario: Default
- **WHEN** a model has no `reasoning` key
- **THEN** its calls request no reasoning
