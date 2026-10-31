-- Описание: Дерево категорий адресатов (groups.parent_id) и счётчик копий в рассылках
-- Миграция выполняется только вперед и фиксируется в таблице schema_version.
--
-- ТЗ §8.12, функция 154: «Категории адресатов: дерево и операции», данные
-- «groups (дерево через parent_id)». Ссылка на родительскую категорию хранится
-- в новой колонке groups.parent_id; циклические ссылки пресекаются триггером
-- схемы и валидатором домена GroupHierarchy.
--
-- ТЗ §3.1.10, функция 140: счётчик «Всего необходимо конвертов» (envelopes_required)
-- вычисляется как сумма copy_count входящих писем — прежнее значение по умолчанию 0
-- заменяется на NULL для явно невычисленных рассылок.

-- ---------------------------------------------------------------------------
-- 1. Дерево категорий: ссылка на родителя
-- ---------------------------------------------------------------------------

ALTER TABLE groups ADD COLUMN parent_id INTEGER NULL REFERENCES groups (id);

CREATE INDEX idx_groups_parent_id ON groups (parent_id);

-- Вставка категории: цикл отклоняется после назначения идентификатора строки.
CREATE TRIGGER trg_groups_no_cycle_insert
AFTER INSERT ON groups
WHEN NEW.parent_id IS NOT NULL
BEGIN
    SELECT RAISE (ABORT, 'Циклическая ссылка в дереве категорий запрещена.')
    WHERE NEW.parent_id = NEW.id
       OR NEW.parent_id IN (
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM groups WHERE id = NEW.id
                UNION
                SELECT child.id FROM groups child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT id FROM subtree
       );
END;

-- Перемещение категории: родителем нельзя назначить саму категорию или её потомка.
-- UNION (без ALL) гарантирует конечность обхода даже на повреждённом дереве.
CREATE TRIGGER trg_groups_no_cycle_update
BEFORE UPDATE OF parent_id ON groups
WHEN NEW.parent_id IS NOT NULL
BEGIN
    SELECT RAISE (ABORT, 'Циклическая ссылка в дереве категорий запрещена.')
    WHERE NEW.parent_id = NEW.id
       OR NEW.parent_id IN (
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM groups WHERE id = NEW.id
                UNION
                SELECT child.id FROM groups child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT id FROM subtree
       );
END;

-- ---------------------------------------------------------------------------
-- 2. Счётчик конвертов рассылки
-- ---------------------------------------------------------------------------

-- Счётчик вычисляется сервисом рассылок (RecalculateEnvelopesRequiredAsync);
-- нулевое значение по умолчанию заменяется на NULL, чтобы «не вычислено» и
-- «вычислено и равно нулю» различались.
UPDATE distributions SET envelopes_required = NULL WHERE envelopes_required = 0;
