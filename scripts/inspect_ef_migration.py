#!/usr/bin/env python3
"""Summarize one EF Core migration and compare it with its target snapshot."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OPERATIONS = {
    "AddColumn", "AddForeignKey", "AlterColumn", "CreateIndex", "CreateTable",
    "DropColumn", "DropForeignKey", "DropIndex", "DropTable", "Sql",
}
DROP_OPERATIONS = {"DropColumn", "DropForeignKey", "DropIndex", "DropTable"}
MAX_SQL_EXCERPT = 240


@dataclass(frozen=True)
class Token:
    kind: str
    value: str
    start: int
    end: int


@dataclass(frozen=True)
class MethodCall:
    name: str
    start: int
    end: int
    arguments: tuple[str, ...]


def _string_start(source: str, index: int) -> tuple[int, int, bool] | None:
    """Return quote position, delimiter length, and verbatim mode."""
    prefix_end = index
    while prefix_end < len(source) and source[prefix_end] in "$@":
        prefix_end += 1
    if prefix_end < len(source) and source[prefix_end] == '"':
        quote_count = 1
        while prefix_end + quote_count < len(source) and source[prefix_end + quote_count] == '"':
            quote_count += 1
        if quote_count >= 3:
            return prefix_end, quote_count, False
        return prefix_end, 1, "@" in source[index:prefix_end]
    if source[index] in "\"'":
        return index, 1, False
    return None


def _scan_string(source: str, index: int, quote_position: int, quote_count: int, verbatim: bool) -> tuple[int, str]:
    quote = source[quote_position]
    content_start = quote_position + quote_count
    if quote_count >= 3:
        terminator = '"' * quote_count
        end_quote = source.find(terminator, content_start)
        if end_quote < 0:
            return len(source), source[content_start:]
        return end_quote + quote_count, source[content_start:end_quote]

    cursor = content_start
    content: list[str] = []
    while cursor < len(source):
        char = source[cursor]
        if char == quote:
            if verbatim and quote == '"' and cursor + 1 < len(source) and source[cursor + 1] == '"':
                content.append('"')
                cursor += 2
                continue
            return cursor + 1, "".join(content)
        if not verbatim and char == "\\" and cursor + 1 < len(source):
            escaped = source[cursor + 1]
            replacements = {"n": "\n", "r": "\r", "t": "\t", "0": "\0", "\\": "\\", '"': '"', "'": "'"}
            if escaped in replacements:
                content.append(replacements[escaped])
                cursor += 2
                continue
            if escaped in {"u", "U", "x"}:
                digits = 4 if escaped == "u" else 8 if escaped == "U" else 0
                digit_start = cursor + 2
                if escaped == "x":
                    digit_end = digit_start
                    while digit_end < min(digit_start + 4, len(source)) and source[digit_end] in "0123456789abcdefABCDEF":
                        digit_end += 1
                else:
                    digit_end = digit_start + digits
                try:
                    content.append(chr(int(source[digit_start:digit_end], 16)))
                    cursor = digit_end
                    continue
                except (ValueError, OverflowError):
                    pass
        content.append(char)
        cursor += 1
    return len(source), "".join(content)


def tokenize(source: str) -> list[Token]:
    """Tokenize enough C# structure to safely balance generated migration calls."""
    tokens: list[Token] = []
    index = 0
    while index < len(source):
        char = source[index]
        if char.isspace():
            index += 1
            continue
        if source.startswith("//", index):
            newline = source.find("\n", index + 2)
            index = len(source) if newline < 0 else newline + 1
            continue
        if source.startswith("/*", index):
            close = source.find("*/", index + 2)
            index = len(source) if close < 0 else close + 2
            continue

        string_start = _string_start(source, index)
        if string_start is not None:
            quote_position, quote_count, verbatim = string_start
            # Prefixes are only part of a literal when immediately followed by its quote.
            if quote_position > index or char in "\"'":
                end, value = _scan_string(source, index, quote_position, quote_count, verbatim)
                tokens.append(Token("STRING", value, index, end))
                index = end
                continue

        if char.isalpha() or char == "_":
            end = index + 1
            while end < len(source) and (source[end].isalnum() or source[end] == "_"):
                end += 1
            tokens.append(Token("IDENT", source[index:end], index, end))
            index = end
            continue

        tokens.append(Token("PUNCT", char, index, index + 1))
        index += 1
    return tokens


def _matching(tokens: list[Token], opening: int) -> int | None:
    pairs = {"(": ")", "[": "]", "{": "}"}
    first = tokens[opening].value
    if first not in pairs:
        return None
    stack = [pairs[first]]
    for index in range(opening + 1, len(tokens)):
        value = tokens[index].value
        if value in pairs:
            stack.append(pairs[value])
        elif stack and value == stack[-1]:
            stack.pop()
            if not stack:
                return index
    return None


def _split_arguments(tokens: list[Token], opening: int, closing: int, source: str) -> tuple[str, ...]:
    if opening + 1 == closing:
        return ()
    ranges: list[tuple[int, int]] = []
    start = opening + 1
    stack: list[str] = []
    pairs = {"(": ")", "[": "]", "{": "}"}
    for index in range(start, closing):
        value = tokens[index].value
        if value in pairs:
            stack.append(pairs[value])
        elif stack and value == stack[-1]:
            stack.pop()
        elif value == "," and not stack:
            ranges.append((start, index))
            start = index + 1
    ranges.append((start, closing))
    return tuple(source[tokens[first].start:tokens[last - 1].end] for first, last in ranges if first < last)


def find_calls(source: str, method: str, receiver: str | None = None) -> list[MethodCall]:
    tokens = tokenize(source)
    calls: list[MethodCall] = []
    for index, token in enumerate(tokens):
        if token.kind != "IDENT" or token.value != method or index < 2 or tokens[index - 1].value != ".":
            continue
        if receiver is not None and tokens[index - 2].value != receiver:
            continue
        cursor = index + 1
        if cursor < len(tokens) and tokens[cursor].value == "<":
            depth = 0
            while cursor < len(tokens):
                if tokens[cursor].value == "<":
                    depth += 1
                elif tokens[cursor].value == ">":
                    depth -= 1
                    if depth == 0:
                        cursor += 1
                        break
                cursor += 1
        if cursor >= len(tokens) or tokens[cursor].value != "(":
            continue
        closing = _matching(tokens, cursor)
        if closing is None:
            continue
        calls.append(MethodCall(
            method,
            token.start,
            tokens[closing].end,
            _split_arguments(tokens, cursor, closing, source),
        ))
    return calls


def _argument_map(call: MethodCall) -> tuple[dict[str, list[str]], list[list[str]]]:
    named: dict[str, list[str]] = {}
    positional: list[list[str]] = []
    for argument in call.arguments:
        tokens = tokenize(argument)
        key = None
        value_tokens = tokens
        if len(tokens) >= 2 and tokens[0].kind == "IDENT" and tokens[1].value == ":":
            key = tokens[0].value
            value_tokens = tokens[2:]
        values = [token.value for token in value_tokens if token.kind == "STRING"]
        if key is None:
            positional.append(values)
        else:
            named[key] = values
    return named, positional


def _strings(argument: str) -> list[str]:
    return [token.value for token in tokenize(argument) if token.kind == "STRING"]


def _statement_end(source: str, start: int) -> int:
    tokens = tokenize(source)
    for token in tokens:
        if token.start >= start and token.value == ";":
            return token.end
    return len(source)


def _extract_lambda_body(argument: str) -> str | None:
    tokens = tokenize(argument)
    for index, token in enumerate(tokens):
        if token.value == "{":
            closing = _matching(tokens, index)
            if closing is not None:
                return argument[token.start:tokens[closing].end]
    return None


def _snake_case(value: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "_", value).lower()


def parse_snapshot(source: str) -> dict:
    """Extract table, column, index, and relationship names from selected model blocks."""
    model = {"tables": {}, "entities": []}
    entities_by_name: dict[str, dict] = {}
    for entity_call in find_calls(source, "Entity", receiver="modelBuilder"):
        if not entity_call.arguments:
            continue
        entity_names = _strings(entity_call.arguments[0])
        if not entity_names:
            continue
        entity_name = entity_names[0]
        body = _extract_lambda_body(entity_call.arguments[1]) if len(entity_call.arguments) > 1 else None
        if body is None:
            continue

        table_calls = find_calls(body, "ToTable", receiver="b")
        table_names = _strings(table_calls[0].arguments[0]) if table_calls and table_calls[0].arguments else []
        existing_entity = entities_by_name.get(entity_name)
        table_name = table_names[0] if table_names else existing_entity.get("table") if existing_entity else None
        properties: list[dict[str, str]] = []
        indexes: list[str] = []
        relationships: list[dict[str, object]] = []

        property_calls = find_calls(body, "Property", receiver="b") + find_calls(body, "PrimitiveCollection", receiver="b")
        for prop_call in property_calls:
            prop_names = _strings(prop_call.arguments[0]) if prop_call.arguments else []
            if not prop_names:
                continue
            statement_end = _statement_end(body, prop_call.end)
            statement = body[prop_call.start:statement_end]
            column_calls = find_calls(statement, "HasColumnName")
            column_names = _strings(column_calls[0].arguments[0]) if column_calls and column_calls[0].arguments else []
            properties.append({"property": prop_names[0], "column": column_names[0] if column_names else _snake_case(prop_names[0])})

        for index_call in find_calls(body, "HasIndex", receiver="b"):
            statement = body[index_call.start:_statement_end(body, index_call.end)]
            names = _strings(index_call.arguments[0]) if index_call.arguments else []
            index_names = find_calls(statement, "HasDatabaseName") or find_calls(statement, "HasName")
            if index_names and index_names[0].arguments:
                names = _strings(index_names[0].arguments[0]) or names
            elif len(index_call.arguments) > 1:
                names = _strings(index_call.arguments[1]) or names
            if names:
                indexes.append(names[0])

        for relationship_call in find_calls(body, "HasOne", receiver="b") + find_calls(body, "HasMany", receiver="b"):
            statement = body[relationship_call.start:_statement_end(body, relationship_call.end)]
            _, positional = _argument_map(relationship_call)
            principal = positional[0][0] if positional and positional[0] else None
            navigation = positional[0][1] if positional and len(positional[0]) > 1 else None
            foreign_key_calls = find_calls(statement, "HasForeignKey")
            constraint_calls = find_calls(statement, "HasConstraintName")
            foreign_keys = _strings(foreign_key_calls[0].arguments[0]) if foreign_key_calls and foreign_key_calls[0].arguments else []
            constraint_names = _strings(constraint_calls[0].arguments[0]) if constraint_calls and constraint_calls[0].arguments else []
            relationship = {
                "principal": principal,
                "navigation": navigation,
                "foreignKeyProperties": foreign_keys,
                "constraint": constraint_names[0] if constraint_names else None,
            }
            relationships.append(relationship)

        entity = existing_entity
        if entity is None:
            entity = {
                "name": entity_name,
                "table": table_name,
                "properties": [],
                "indexes": [],
                "relationships": [],
            }
            entities_by_name[entity_name] = entity
            model["entities"].append(entity)
        elif entity["table"] is None:
            entity["table"] = table_name
        for prop in properties:
            if prop not in entity["properties"]:
                entity["properties"].append(prop)
        for index_name in indexes:
            if index_name not in entity["indexes"]:
                entity["indexes"].append(index_name)
        for relationship in relationships:
            if relationship not in entity["relationships"]:
                entity["relationships"].append(relationship)
        if table_name:
            table = model["tables"].setdefault(table_name, {"columns": set(), "indexes": set(), "foreignKeys": set()})
            table["columns"].update(prop["column"] for prop in properties)
            table["indexes"].update(indexes)
            table["foreignKeys"].update(rel["constraint"] for rel in relationships if rel["constraint"])

    return model


def _method_body(source: str, method: str) -> str | None:
    tokens = tokenize(source)
    for index, token in enumerate(tokens):
        if token.kind != "IDENT" or token.value != method:
            continue
        opening = index + 1
        if opening >= len(tokens) or tokens[opening].value != "(":
            continue
        closing = _matching(tokens, opening)
        if closing is None:
            continue
        body_start = closing + 1
        while body_start < len(tokens) and tokens[body_start].value != "{":
            body_start += 1
        if body_start == len(tokens):
            continue
        body_end = _matching(tokens, body_start)
        if body_end is not None:
            return source[tokens[body_start].start:tokens[body_end].end]
    return None


def _operation(call: MethodCall) -> dict:
    named, positional = _argument_map(call)

    def first(key: str, position: int = 0) -> str | None:
        values = named.get(key)
        if values:
            return values[0]
        return positional[position][0] if position < len(positional) and positional[position] else None

    operation = {"operation": call.name, "name": first("name"), "table": first("table"), "columns": []}
    if call.name in {"CreateTable", "DropTable"}:
        operation["table"] = operation["table"] or operation["name"] or first("name", 0)
    if call.name in {"AddColumn", "DropColumn", "AlterColumn"}:
        operation["column"] = operation["name"]
    if call.name == "Sql":
        sql = next((value for values in positional for value in values), None)
        if sql is None:
            sql = next((value for values in named.values() for value in values), "")
        operation.pop("name", None)
        operation.pop("table", None)
        operation["sql"] = sql
    elif call.name in {"CreateTable", "AddForeignKey", "AddColumn", "AlterColumn"} and "column" in named:
        operation["columns"] = named["column"][:]
    elif call.name in {"AddIndex", "CreateIndex", "DropIndex"} and "columns" in named:
        operation["columns"] = named["columns"][:]
    operation["destructive"] = call.name in DROP_OPERATIONS or call.name == "AlterColumn"
    return operation


def _extract_operations(source: str) -> dict[str, list[dict]]:
    result: dict[str, list[dict]] = {}
    for direction in ("Up", "Down"):
        body = _method_body(source, direction)
        calls = [] if body is None else [
            call
            for operation_name in OPERATIONS
            for call in find_calls(body, operation_name, receiver="migrationBuilder")
        ]
        result[direction] = [_operation(call) for call in sorted(calls, key=lambda item: item.start)]
    return result


def _compare_with_snapshot(operations: dict[str, list[dict]], model: dict) -> list[dict]:
    warnings = []
    # The snapshot describes the result after Up. Down is expected to restore the older model.
    for operation in operations.get("Up", []):
        table_name = operation.get("table")
        table = model["tables"].get(table_name)
        if table is None:
            continue
        detail = None
        if operation["operation"] == "DropTable":
            detail = f"table {table_name!r} remains in the target snapshot"
        elif operation["operation"] == "DropColumn" and operation.get("column") in table["columns"]:
            detail = f"column {table_name}.{operation['column']} remains in the target snapshot"
        elif operation["operation"] == "DropIndex" and operation.get("name") in table["indexes"]:
            detail = f"index {operation['name']!r} remains on active table {table_name!r}"
        elif operation["operation"] == "DropForeignKey" and operation.get("name") in table["foreignKeys"]:
            detail = f"foreign key {operation['name']!r} remains on active table {table_name!r}"
        if detail:
            warnings.append({"direction": "Up", "operation": operation, "message": detail})
    return warnings


def _selected_entities(model: dict, requested: list[str]) -> tuple[list[dict], list[str]]:
    if not requested:
        return [], []
    wanted = {name.casefold() for name in requested}
    selected = [
        entity for entity in model["entities"]
        if entity["name"].casefold() in wanted or entity["name"].rsplit(".", 1)[-1].casefold() in wanted
    ]
    found = {entity["name"].casefold() for entity in selected}
    found.update(entity["name"].rsplit(".", 1)[-1].casefold() for entity in selected)
    missing = [name for name in requested if name.casefold() not in found]
    return selected, missing


def _compact_sql(sql: str, limit: int = MAX_SQL_EXCERPT) -> str:
    compacted = re.sub(r"\s+", " ", sql).strip()
    return compacted if len(compacted) <= limit else compacted[:limit].rstrip() + "..."


def analyze(migration_path: Path, snapshot_path: Path | None, entities: list[str], full_sql: bool = False) -> dict:
    if migration_path.name.endswith(".Designer.cs"):
        raise ValueError("pass a migration .cs file, not a historical *.Designer.cs snapshot")
    source = migration_path.read_text(encoding="utf-8")
    operations = _extract_operations(source)
    model = parse_snapshot(snapshot_path.read_text(encoding="utf-8")) if snapshot_path else {"tables": {}, "entities": []}
    selected, missing = _selected_entities(model, entities)

    sql_blocks = []
    destructive = []
    for direction, direction_operations in operations.items():
        for operation in direction_operations:
            if operation["operation"] == "Sql":
                sql = operation.pop("sql")
                block = {"direction": direction, "excerpt": _compact_sql(sql)}
                if full_sql:
                    block["sql"] = sql
                sql_blocks.append(block)
            if operation["destructive"]:
                destructive.append({"direction": direction, **operation})

    return {
        "migration": str(migration_path),
        "snapshot": str(snapshot_path) if snapshot_path else None,
        "operations": operations,
        "destructive": destructive,
        "suspicious": _compare_with_snapshot(operations, model),
        "rawSqlCount": len(sql_blocks),
        "rawSql": sql_blocks,
        "entities": selected,
        "missingEntities": missing,
    }


def _operation_label(operation: dict) -> str:
    label = operation["operation"]
    if operation.get("table"):
        label += f" {operation['table']}"
    if operation.get("column"):
        label += f".{operation['column']}"
    elif operation.get("name") and operation.get("table") != operation.get("name"):
        label += f" {operation['name']}"
    if operation.get("columns"):
        label += " (" + ", ".join(operation["columns"]) + ")"
    return label


def render_text(report: dict, full_sql: bool = False) -> str:
    lines = [f"Migration: {report['migration']}", f"Target snapshot: {report['snapshot'] or '(not supplied)'}"]
    for direction in ("Up", "Down"):
        operations = report["operations"][direction]
        lines.append(f"\n{direction} ({len(operations)} operation(s)):")
        lines.extend(f"  - {_operation_label(operation)}" for operation in operations)
        if not operations:
            lines.append("  (no recognized operations)")

    lines.append(f"\nDestructive / potentially destructive: {len(report['destructive'])}")
    lines.extend(f"  - {item['direction']}: {_operation_label(item)}" for item in report["destructive"])
    lines.append(f"\nSuspicious against target snapshot: {len(report['suspicious'])}")
    lines.extend(f"  - {item['direction']}: {item['message']}" for item in report["suspicious"])

    lines.append(f"\nRaw SQL: {report['rawSqlCount']} block(s)")
    for block in report["rawSql"]:
        text = block.get("sql") if full_sql else block["excerpt"]
        lines.append(f"  - {block['direction']}: {text}")

    if report["entities"] or report["missingEntities"]:
        lines.append("\nSelected entities:")
        for entity in report["entities"]:
            lines.append(f"  - {entity['name']} -> {entity['table'] or '(no table mapping)'}")
            lines.extend(f"      {item['property']} -> {item['column']}" for item in entity["properties"])
            lines.extend(f"      index -> {index_name}" for index_name in entity["indexes"])
            for relation in entity["relationships"]:
                target = relation["principal"] or "?"
                foreign_key = ", ".join(relation["foreignKeyProperties"]) or "?"
                name = f" [{relation['constraint']}]" if relation["constraint"] else ""
                lines.append(f"      relationship -> {target} via {foreign_key}{name}")
        lines.extend(f"  - NOT FOUND: {name}" for name in report["missingEntities"])
    return "\n".join(lines) + "\n"


def _output_path(value: str) -> Path:
    artifact_dir = ROOT / ".test-tmp"
    requested = Path(value)
    if requested.is_absolute():
        destination = requested.resolve()
    elif requested.parts and requested.parts[0] == ".test-tmp":
        destination = (ROOT / requested).resolve()
    else:
        destination = (artifact_dir / requested).resolve()
    try:
        destination.relative_to(artifact_dir.resolve())
    except ValueError as error:
        raise ValueError("--output must stay inside the repository .test-tmp/ directory") from error
    return destination


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("migration", type=Path, help="one migration .cs file (never a *.Designer.cs file)")
    parser.add_argument("--snapshot", type=Path, help="target ModelSnapshot.cs; defaults to the adjacent snapshot when present")
    parser.add_argument("--entity", action="append", default=[], help="entity name to extract; repeat to select several")
    parser.add_argument("--format", choices=("text", "json"), default="text", help="report format (default: text)")
    parser.add_argument("--full-sql", action="store_true", help="include complete migrationBuilder.Sql blocks")
    parser.add_argument("--output", help="write report under .test-tmp/; a relative name is placed there")
    args = parser.parse_args(argv)
    if args.snapshot is None:
        adjacent = args.migration.parent / "MediaDockDbContextModelSnapshot.cs"
        args.snapshot = adjacent if adjacent.is_file() else None
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        report = analyze(args.migration, args.snapshot, args.entity, args.full_sql)
        output = json.dumps(report, indent=2, ensure_ascii=False) + "\n" if args.format == "json" else render_text(report, args.full_sql)
        if args.output:
            destination = _output_path(args.output)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(output, encoding="utf-8")
            print(f"Report written to {destination.relative_to(ROOT).as_posix()}")
        else:
            sys.stdout.write(output)
        return 0
    except (OSError, ValueError) as error:
        print(f"Could not inspect EF migration: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())