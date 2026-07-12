-- 在 20260711010000_EnforceDatabaseCollation 前执行。
-- 失败时 SIGNAL 会使 mysql 客户端以非零状态退出。
-- 读取全局事务与锁等待需要 PROCESS 权限；应使用容器内 root/运维账号，禁止给应用账号扩权。
SET @target_schema = DATABASE();

SELECT COALESCE(SUM(DATA_LENGTH + INDEX_LENGTH), 0) AS estimated_database_bytes
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = @target_schema;

SET @active_transactions = (SELECT COUNT(*) FROM information_schema.INNODB_TRX);
SET @lock_waits = (SELECT COUNT(*) FROM performance_schema.data_lock_waits);

DELIMITER //
DROP PROCEDURE IF EXISTS preflight_collation_migration//
CREATE PROCEDURE preflight_collation_migration()
BEGIN
    DECLARE done INT DEFAULT 0;
    DECLARE candidate_table VARCHAR(64);
    DECLARE candidate_index VARCHAR(64);
    DECLARE grouping_expression TEXT;
    DECLARE collision_count BIGINT DEFAULT 0;
    DECLARE unique_indexes CURSOR FOR
        SELECT s.TABLE_NAME,
               s.INDEX_NAME,
               GROUP_CONCAT(
                   CASE
                       WHEN c.DATA_TYPE IN ('char', 'varchar', 'tinytext', 'text', 'mediumtext', 'longtext', 'enum', 'set')
                           THEN CONCAT('CONVERT(`', REPLACE(s.COLUMN_NAME, '`', '``'), '` USING utf8mb4) COLLATE utf8mb4_unicode_ci')
                       ELSE CONCAT('`', REPLACE(s.COLUMN_NAME, '`', '``'), '`')
                   END
                   ORDER BY s.SEQ_IN_INDEX SEPARATOR ', '
               )
        FROM information_schema.STATISTICS s
        JOIN information_schema.COLUMNS c
          ON c.TABLE_SCHEMA = s.TABLE_SCHEMA
         AND c.TABLE_NAME = s.TABLE_NAME
         AND c.COLUMN_NAME = s.COLUMN_NAME
        WHERE s.TABLE_SCHEMA = @target_schema
          AND s.NON_UNIQUE = 0
          AND s.INDEX_NAME <> 'PRIMARY'
        GROUP BY s.TABLE_NAME, s.INDEX_NAME
        HAVING SUM(c.DATA_TYPE IN ('char', 'varchar', 'tinytext', 'text', 'mediumtext', 'longtext', 'enum', 'set')) > 0;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

    IF @active_transactions > 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'preflight failed: active InnoDB transactions exist';
    END IF;
    IF @lock_waits > 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'preflight failed: metadata/data lock waits exist';
    END IF;

    OPEN unique_indexes;
    collision_loop: LOOP
        FETCH unique_indexes INTO candidate_table, candidate_index, grouping_expression;
        IF done = 1 THEN
            LEAVE collision_loop;
        END IF;

        SET @collision_count = 0;
        SET @collision_sql = CONCAT(
            'SELECT COUNT(*) INTO @collision_count FROM (SELECT 1 FROM `',
            REPLACE(candidate_table, '`', '``'),
            '` GROUP BY ', grouping_expression,
            ' HAVING COUNT(*) > 1 LIMIT 1) AS collisions');
        PREPARE collision_statement FROM @collision_sql;
        EXECUTE collision_statement;
        DEALLOCATE PREPARE collision_statement;
        IF @collision_count > 0 THEN
            SET @message = CONCAT('preflight failed: target collation conflicts in ', candidate_table, '.', candidate_index);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = @message;
        END IF;
    END LOOP;
    CLOSE unique_indexes;
END//
DELIMITER ;

CALL preflight_collation_migration();
DROP PROCEDURE preflight_collation_migration;
