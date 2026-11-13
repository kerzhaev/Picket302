-- Описание: защита от двойного приёма входящего документа (функция 30)
-- S33 (ревью D-S33-05): частичный уникальный индекс — не более одного
-- движения «приём» на документ; гонка check-then-insert исключается на
-- уровне схемы, а не только кода.
CREATE UNIQUE INDEX IF NOT EXISTS idx_incoming_movements_one_reception
    ON incoming_movements (incoming_id)
    WHERE action = 0;
