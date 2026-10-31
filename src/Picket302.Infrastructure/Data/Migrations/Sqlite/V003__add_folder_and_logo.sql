-- Описание: Связь отправлений с папками, логотип контрагента и пресечение циклов в дереве папок (ТЗ §8.1, функции 22 и 47; §3.1.11)
-- Миграция выполняется только вперед и фиксируется в таблице schema_version.

-- Связь письма с папкой пользовательской классификации (ТЗ §8.1, функция 22:
-- «Таблица folders, колонка mails.folder_id»).
ALTER TABLE mails ADD COLUMN folder_id INTEGER NULL REFERENCES folders (id);

-- Логотип компании контрагента — растровый файл (ТЗ §8.1, функция 47:
-- «Таблица counterparties (колонка logo)»).
ALTER TABLE counterparties ADD COLUMN logo BLOB NULL;

CREATE INDEX idx_mails_folder_id ON mails (folder_id);

-- Пресечение циклических ссылок parent_id в дереве папок (ТЗ §3.1.11:
-- «циклические ссылки parent_id пресекаются триггером/валидатором домена»).
-- Родителем папки нельзя назначить саму папку или её собственного потомка.
CREATE TRIGGER trg_folders_no_cycle_insert
BEFORE INSERT ON folders
WHEN NEW.parent_id IS NOT NULL
BEGIN
    SELECT RAISE (ABORT, 'Циклическая ссылка в дереве папок запрещена.')
    WHERE NEW.parent_id = NEW.id
       OR NEW.parent_id IN (
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM folders WHERE id = NEW.id
                UNION ALL
                SELECT child.id FROM folders child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT id FROM subtree
       );
END;

CREATE TRIGGER trg_folders_no_cycle_update
BEFORE UPDATE OF parent_id ON folders
WHEN NEW.parent_id IS NOT NULL
BEGIN
    SELECT RAISE (ABORT, 'Циклическая ссылка в дереве папок запрещена.')
    WHERE NEW.parent_id = NEW.id
       OR NEW.parent_id IN (
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM folders WHERE id = NEW.id
                UNION ALL
                SELECT child.id FROM folders child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT id FROM subtree
       );
END;
