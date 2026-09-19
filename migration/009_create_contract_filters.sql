CREATE TABLE contract_filter_rules
(
	contract_id uuid    NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,
	scope       text    NOT NULL,
	rule_order  integer NOT NULL,
	selector    text    NOT NULL,
	operator    text    NOT NULL,
	value       text    NULL,
	values_json jsonb   NOT NULL DEFAULT '[]'::jsonb,
	PRIMARY KEY (contract_id, scope, rule_order),
	CONSTRAINT ck_contract_filter_rules_scope CHECK (scope IN ('metadata', 'payload')),
	CONSTRAINT ck_contract_filter_rules_rule_order CHECK (rule_order >= 0),
	CONSTRAINT ck_contract_filter_rules_selector_not_empty CHECK (char_length(trim(selector)) > 0),
	CONSTRAINT ck_contract_filter_rules_operator_not_empty CHECK (char_length(trim(operator)) > 0),
	CONSTRAINT ck_contract_filter_rules_values_json_is_array CHECK (jsonb_typeof(values_json) = 'array')
);

CREATE INDEX ix_contract_filter_rules_contract_scope
	ON contract_filter_rules(contract_id, scope, rule_order);
